// See the matching header in InputInjector.cs: ENABLE_INPUT_SYSTEM only means
// Active Input Handling is New/Both; ENABLE_INPUT_SYSTEM_PACKAGE (asmdef
// versionDefine) means com.unity.inputsystem is installed. Both must hold.
#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
#define USE_INPUT_SYSTEM
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GamePeek
{
    // ── State & data model ────────────────────────────────────────────────────

    /// <summary>Connection state of the GamePeek plugin.</summary>
    public enum ConnectionState
    {
        /// <summary>Server is stopped; no clients connected.</summary>
        Disconnected,
        /// <summary>Server is running and advertising via mDNS; waiting for a device.</summary>
        Advertising,
        /// <summary>At least one device is connected and streaming is active.</summary>
        Connected,
    }

    /// <summary>Describes a connected device.</summary>
    public sealed class DeviceInfo
    {
        /// <summary>TCP control-channel session identifier.</summary>
        public int    SessionId  { get; init; }
        /// <summary>Device name from the HELLO payload ("Unknown" until the handshake).</summary>
        public string DeviceName { get; init; }
        /// <summary>IP address of the remote device (empty string if unknown).</summary>
        public string IPAddress  { get; init; }
        /// <summary>UTC time when the device connected.</summary>
        public DateTime ConnectedAt { get; init; }
        /// <summary>Whether the device has a Pro tier subscription.</summary>
        public bool IsPro { get; init; }
        /// <summary>Whether the session has completed the HELLO handshake.</summary>
        public bool HelloReceived { get; init; }
    }

    // ── Streaming configuration ───────────────────────────────────────────────

    /// <summary>All runtime-adjustable streaming parameters.</summary>
    public sealed class StreamConfig
    {
        public int Width   { get; set; } = 1280;
        public int Height  { get; set; } = 720;
        public int Quality { get; set; } = 75;
        public int FpsCap  { get; set; } = 30;

        // Input-enable gates from CONFIG (PROTOCOL.md §4.3). Default enabled —
        // matches v1, and a CONFIG that omits them keeps them on. Read from
        // socket threads (bool reads are atomic; staleness is harmless).
        public bool TouchEnabled { get; set; } = true;
        public bool GyroEnabled  { get; set; } = true;
        public bool AccelEnabled { get; set; } = true;
    }

    // ── Manager ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Central orchestrator for GamePeek. Manages the full lifecycle of the
    /// TCP control server, UDP video sender, mDNS advertiser, frame capture,
    /// and frame encoder (wire protocol v2 — see docs~/PROTOCOL.md).
    /// <para>
    /// All state-change events are guaranteed to fire on the Unity <em>main
    /// thread</em> via a <see cref="ConcurrentQueue{T}"/> drained on each
    /// <see cref="EditorApplication.update"/> tick.
    /// </para>
    /// </summary>
    public sealed class ConnectionManager : IDisposable
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static ConnectionManager _instance;
        /// <summary>Returns the single shared instance, creating it if necessary.</summary>
        public static ConnectionManager Instance
            => _instance ??= new ConnectionManager();

        // ── Events (main-thread) ──────────────────────────────────────────────
        /// <summary>Fired whenever the connection state changes.</summary>
        public event Action<ConnectionState>   StateChanged;
        /// <summary>Fired when a device successfully connects.</summary>
        public event Action<DeviceInfo>        DeviceConnected;
        /// <summary>Fired when a device disconnects.</summary>
        public event Action<DeviceInfo>        DeviceDisconnected;
        /// <summary>Fired on each FPS stats update (≈ once per second).</summary>
        public event Action<float, float>      StatsUpdated;  // (captureFps, encodeMs)
        /// <summary>Fired when the phone-reported RTT changes. Arg is RTT in milliseconds.</summary>
        public event Action<float>             RttUpdated;

        // ── Observed state (main-thread readable) ─────────────────────────────
        /// <summary>Current connection state.</summary>
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>The TCP port the control server is (or will be) listening on.</summary>
        public int Port { get; private set; } = GamePeekConstants.DefaultPort;

        /// <summary>Read-only view of all currently connected devices.</summary>
        public IReadOnlyList<DeviceInfo> ConnectedDevices => _devices;

        /// <summary>Active streaming configuration.</summary>
        public StreamConfig Config { get; } = new StreamConfig();

        /// <summary>
        /// RTT in milliseconds as measured by the host phone (STATS.rttMs,
        /// 1 Hz). 0 until the first report — the editor sends no pings in v2.
        /// </summary>
        public float SmoothedRtt { get; private set; }

        /// <summary>Active frame capture strategy.</summary>
        public CaptureMethod ActiveCaptureMethod { get; private set; } = CaptureMethod.CameraRender;

        // ── Internal components ───────────────────────────────────────────────
        private TcpControlServer _tcpServer;
        private UdpVideoSender   _udpSender;
        private MdnsAdvertiser   _mdns;
        private FrameCapture     _capture;
        private FrameEncoder     _encoder;
        private StreamAdaptation _adaptation;

        private readonly List<DeviceInfo>       _devices         = new();
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        /// <summary>Sentinel for "no session" (real session ids start at 1).</summary>
        private const int NoSession = -1;

        // First session to send a valid HELLO becomes host; only it can send
        // config/input. Written on the main thread; read from socket threads
        // for sensor gating, hence volatile.
        private volatile int _hostSessionId = NoSession;

        // Suppress repeated "not in Play Mode" warnings for touch input.
        private bool _gameViewFocusWarningLogged;

        // One off-spec SENSOR-size warning per streaming session (reset in
        // StartStreaming — the manager itself is a long-lived singleton).
        private bool _sensorSizeWarned;

        private bool   _editorHooked;
        // Time.unscaledDeltaTime is unreliable in edit mode — timers use
        // EditorApplication.timeSinceStartup deltas (same pattern as FrameCapture).
        private double _lastStatsTime;

        // ── Domain-reload survival (SessionState keys) ────────────────────────
        // SessionState survives domain reloads but not editor restarts. The live
        // streaming state is snapshotted here in OnBeforeAssemblyReload and claimed
        // by GamePeekSessionRestore after the reload, so the server comes back up
        // on the same port and phones can auto-reconnect. Tokens and the UDP port
        // are deliberately NOT persisted: the reload drops every TCP session, and
        // the fresh HELLO→WELCOME handshake issues new ones (PROTOCOL.md §3).
        internal const string SessionKeyResumeStreaming     = "GamePeek_Resume_Streaming";
        internal const string SessionKeyResumePort          = "GamePeek_Resume_Port";
        internal const string SessionKeyResumeCaptureMethod = "GamePeek_Resume_CaptureMethod";
        internal const string SessionKeyResumeWidth         = "GamePeek_Resume_Width";
        internal const string SessionKeyResumeHeight        = "GamePeek_Resume_Height";
        internal const string SessionKeyResumeQuality       = "GamePeek_Resume_Quality";
        internal const string SessionKeyResumeFps           = "GamePeek_Resume_Fps";

        // Game View size the user had selected before GamePeek switched to the
        // "GamePeekCapture" slot (-1 / unset = nothing to restore). Unlike the
        // resume keys above this is NOT written in OnBeforeAssemblyReload — it is
        // captured on the first resize of a streaming session and only cleared
        // when an intentional stop restores the selection, so it survives
        // domain reloads mid-stream.
        internal const string SessionKeyPrevGameViewSize    = "GamePeek_Prev_GameViewSizeIndex";

        // ── Constructor / dispose ─────────────────────────────────────────────

        private ConnectionManager()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnBeforeAssemblyReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

            bool wasStreaming = State != ConnectionState.Disconnected;

            // Close WITHOUT the SHUTDOWN broadcast — SHUTDOWN tells the phone the
            // stream ended intentionally (clean exit, no reconnect). A plain FIN
            // makes it treat the reload as a drop and auto-reconnect once the
            // server is back up (restarted by GamePeekSessionRestore after the
            // reload) — PROTOCOL.md §3 "Teardown".
            StopStreaming(notifyClients: false);

            if (!wasStreaming) return;

            // Snapshot the live state AFTER StopStreaming (which erases the resume
            // flag) so GamePeekSessionRestore can bring streaming back up.
            SessionState.SetBool(SessionKeyResumeStreaming, true);
            SessionState.SetInt(SessionKeyResumePort,          Port);
            SessionState.SetInt(SessionKeyResumeCaptureMethod, (int)ActiveCaptureMethod);
            SessionState.SetInt(SessionKeyResumeWidth,         Config.Width);
            SessionState.SetInt(SessionKeyResumeHeight,        Config.Height);
            SessionState.SetInt(SessionKeyResumeQuality,       Config.Quality);
            SessionState.SetInt(SessionKeyResumeFps,           Config.FpsCap);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopStreaming();
            _instance = null;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the TCP control server, UDP video socket, and mDNS advertiser.
        /// Also auto-configures the Windows firewall on first run.
        /// <para>
        /// When <paramref name="port"/> is already taken, binding is retried on
        /// <c>port+1 … port+9</c> before giving up. On success <see cref="Port"/>
        /// holds the port actually bound — the mDNS advertisement, the connection
        /// QR, the window UI, and the domain-reload resume snapshot all read it.
        /// The UDP socket binds the same number where possible (PROTOCOL.md §2);
        /// WELCOME carries the authoritative value either way.
        /// </para>
        /// </summary>
        public bool StartStreaming(int port = GamePeekConstants.DefaultPort)
        {
            if (State != ConnectionState.Disconnected) return true;

            _sensorSizeWarned = false;

            try
            {
                // Boot the control server — retry on the next few ports when the
                // requested one is taken (e.g. another editor instance).
                Exception lastBindError = null;
                for (int candidate = port;
                     candidate < port + GamePeekConstants.PortBindAttempts && candidate <= 65535;
                     candidate++)
                {
                    var server = new TcpControlServer(candidate);
                    server.ClientConnected    += OnClientConnected;
                    server.ClientDisconnected += OnClientDisconnected;
                    server.MessageReceived    += OnTcpMessage;
                    try
                    {
                        server.Start();
                    }
                    catch (Exception ex)
                    {
                        lastBindError = ex;
                        continue; // nothing left running after a failed Start; try the next port
                    }
                    _tcpServer = server;
                    Port       = candidate;
                    break;
                }

                if (_tcpServer == null)
                    throw lastBindError ?? new InvalidOperationException("No bindable port found.");

                if (Port != port)
                {
                    // The QR texture cache only tracks IP changes — force a
                    // regeneration so the payload carries the fallback port.
                    QRCodeGenerator.Invalidate();
                    Debug.LogWarning(
                        $"[GamePeek] [TCP] Port {port} is in use — streaming on fallback port {Port} instead. " +
                        "The QR code and mDNS advertisement use the new port; devices connecting by IP must use it too.");
                }

                // One-time Windows firewall setup (TCP + UDP inbound). Pass the
                // REQUESTED base port: the rules cover the whole retry range
                // [port, port+attempts-1], so any fallback port bound above is
                // already allowed.
                FirewallHelper.EnsureFirewallRule(port);

                // Boot the UDP video socket — binds the bound TCP port number
                // where possible so the firewall range rule covers it (§2).
                var udp = new UdpVideoSender();
                udp.TransportChanged += OnTransportChanged;
                udp.SensorReceived   += OnUdpSensorReceived;
                udp.Start(Port, _tcpServer);
                _udpSender = udp;

                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

                // Boot mDNS
                string localIp    = QRCodeGenerator.GetLocalIPv4();
                string editorName = EditorPrefs.GetString(GamePeekConstants.PrefEditorName, string.Empty);
                _mdns = new MdnsAdvertiser(Port, localIp, editorName);
                _mdns.Start();

                // Boot encoder + capture
                _encoder = new FrameEncoder(_udpSender, Config.Quality);
                _capture = new FrameCapture(_encoder, Config.Width, Config.Height, Config.FpsCap);
                _capture.SetCaptureMethod(ActiveCaptureMethod);
                _capture.Start();

                // STATS-driven adaptation (phase 3) — pacing/quality/fps levers.
                _adaptation = new StreamAdaptation(_encoder, _capture, _udpSender);
                _adaptation.OnConfigApplied(Config.Quality, Config.FpsCap);

                HookEditorUpdate();
                SetState(ConnectionState.Advertising);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GamePeek] [TCP] Failed to start GamePeek on ports {port}–{port + GamePeekConstants.PortBindAttempts - 1}. Other processes may already be using them. You can change the port in the GamePeek editor window.");
                Debug.LogException(ex);
                StopStreaming();
                return false;
            }
        }

        /// <summary>
        /// Sends SHUTDOWN to a single client and closes its connection.
        /// All other clients remain connected and streaming continues.
        /// </summary>
        public void DisconnectDevice(int sessionId)
        {
            if (_tcpServer == null) return;
            _tcpServer.Send(sessionId, MsgType.Shutdown);
            // Close on the next tick so the message flushes first (§4.6 pattern).
            var server = _tcpServer;
            EditorApplication.delayCall += () => server?.CloseSession(sessionId);
        }

        /// <summary>Stops all streaming, severs all connections, and releases resources.</summary>
        /// <param name="notifyClients">
        /// When <c>true</c> (default — an intentional stop) clients receive a
        /// SHUTDOWN message so they exit cleanly without reconnecting.
        /// Pass <c>false</c> on domain-reload teardown so the phone treats the
        /// close as a drop and auto-reconnects once the server is back up.
        /// </param>
        public void StopStreaming(bool notifyClients = true)
        {
            if (State == ConnectionState.Disconnected &&
                _tcpServer == null &&
                _mdns == null &&
                _capture == null)
                return;

            // An intentional stop cancels any pending after-reload auto-resume.
            // (OnBeforeAssemblyReload re-sets the flag after calling this method.)
            SessionState.EraseBool(SessionKeyResumeStreaming);

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            _capture?.Stop();
            _capture = null;

            _encoder    = null;
            _adaptation = null;

            // Tell all connected clients the stream is ending before closing
            // the sockets. SHUTDOWN is terminal-but-friendly: the app exits
            // cleanly instead of starting its reconnect countdown.
            if (notifyClients)
                _tcpServer?.Broadcast(MsgType.Shutdown);

            _udpSender?.Stop();
            _udpSender = null;

            _tcpServer?.Stop();
            _tcpServer = null;

            _mdns?.Stop();
            _mdns = null;

            // Restore the Game View size the user had selected before GamePeek
            // forced the "GamePeekCapture" slot. Only on an intentional stop —
            // on domain-reload teardown streaming resumes right after, and the
            // SessionState key survives the reload so a later manual stop still
            // restores the original selection. The custom slot itself is kept;
            // recreating it every session would churn the GameViewSizes asset.
            if (notifyClients) RestorePreviousGameViewSize();

            _devices.Clear();
            _hostSessionId = NoSession;
            SmoothedRtt = 0f;

            UnhookEditorUpdate();
            QRCodeGenerator.Invalidate();
            SetState(ConnectionState.Disconnected);

#if USE_INPUT_SYSTEM
            InputInjector.RemoveVirtualDevices();
#endif
        }

        /// <summary>Switches the frame capture strategy at runtime.</summary>
        public void SetCaptureMethod(CaptureMethod method)
        {
            ActiveCaptureMethod = method;
            _capture?.SetCaptureMethod(method);
        }

        /// <summary>
        /// Applies a <see cref="StreamConfig"/> received from the phone app and
        /// updates the capture + encoder components accordingly.
        /// </summary>
        public void ApplyConfig(int width, int height, int quality, int fpsCap)
        {
            Config.Width   = width;
            Config.Height  = height;
            Config.Quality = quality;
            Config.FpsCap  = fpsCap;

            _capture?.SetResolution(width, height);
            _capture?.SetFpsCap(fpsCap);
            _encoder?.SetQuality(quality);

            // A CONFIG expresses fresh user intent — adaptation restarts from
            // the requested quality/fps as its new ceilings.
            _adaptation?.OnConfigApplied(quality, fpsCap);

            // Resize the Game View so ScreenCapture captures at the phone's exact
            // resolution — avoids stretching when aspect ratios differ.
            TrySetGameViewResolution(width, height);
        }

        // Warn only once per mismatch streak when the Game View refuses the requested size.
        private static bool _resizeMismatchWarned;

        /// <summary>
        /// Sets the Unity Game View to a custom fixed resolution using
        /// <see cref="GameViewSize"/>.
        /// </summary>
        private static void TrySetGameViewResolution(int width, int height)
        {
            try
            {
                // Remember what the user had selected before the first switch to
                // the "GamePeekCapture" slot so StopStreaming can restore it.
                // Captured once per streaming session (repeated config messages
                // must not overwrite it with the capture slot itself) and kept in
                // SessionState so it survives domain reloads mid-stream.
                if (SessionState.GetInt(SessionKeyPrevGameViewSize, -1) < 0)
                {
                    int current = GameViewSize.GetSelectedIndex();
                    if (current >= 0)
                        SessionState.SetInt(SessionKeyPrevGameViewSize, current);
                }

                var sizeObj = GameViewSize.SetCustomSize(width, height);
                GameViewSize.SelectSize(sizeObj);
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Config] Could not resize Game View: {ex.Message}");
            }

            // The reflection above can also fail silently (Unity internals change
            // between versions) — read back the actual rendering resolution on the
            // next tick, after the Game View has applied the new size.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    PlayModeWindow.GetRenderingResolution(out uint actualW, out uint actualH);
                    if (actualW == (uint)width && actualH == (uint)height)
                    {
                        _resizeMismatchWarned = false;
                    }
                    else if (!_resizeMismatchWarned)
                    {
                        _resizeMismatchWarned = true;
                        Debug.LogWarning(
                            $"[GamePeek] [Config] Game View resize to {width}x{height} did not apply " +
                            $"(actual rendering resolution: {actualW}x{actualH}). Touch mapping uses the " +
                            "actual rendering size so taps still align, but the stream aspect may differ " +
                            "from the phone.");
                    }
                }
                catch (Exception ex)
                {
                    GamePeekConstants.LogWarning($"[Config] Could not verify Game View resolution: {ex.Message}");
                }
            };
        }

        /// <summary>
        /// Re-selects the Game View size that was active before
        /// <see cref="TrySetGameViewResolution"/> switched to the
        /// "GamePeekCapture" slot, then clears the stored index.
        /// </summary>
        private static void RestorePreviousGameViewSize()
        {
            int prevIndex = SessionState.GetInt(SessionKeyPrevGameViewSize, -1);
            if (prevIndex < 0) return;

            SessionState.EraseInt(SessionKeyPrevGameViewSize);
            try
            {
                GameViewSize.SelectIndex(prevIndex);
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Config] Could not restore Game View size: {ex.Message}");
            }
        }

        // ── Editor update hook ────────────────────────────────────────────────

        private void HookEditorUpdate()
        {
            if (_editorHooked) return;
            _lastStatsTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            _editorHooked = true;
        }

        private void UnhookEditorUpdate()
        {
            if (!_editorHooked) return;
            EditorApplication.update -= OnEditorUpdate;
            _editorHooked = false;
        }

        private void OnEditorUpdate()
        {
            // Drain cross-thread callbacks
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { GamePeekConstants.LogError(ex.ToString()); }
            }

            // Sessions whose 2 s punch window expired fall back to video-over-TCP
            // (§3). Cheap — iterates the handful of connected peers.
            _udpSender?.ApplyPunchDeadlines();

            double now = EditorApplication.timeSinceStartup;

            // ~1 Hz housekeeping: stats update + dead-peer sweep.
            if (now - _lastStatsTime >= 1.0)
            {
                _lastStatsTime = now;

                if (_capture != null && _encoder != null)
                    StatsUpdated?.Invoke(_capture.SmoothedFps, _encoder.LastEncodeMs);

                // Liveness (§3): the phone PINGs every 2 s, so 10 s of TCP
                // silence means a dead peer — drop it so a zombie session can't
                // hold the host role or the free-tier device slot.
                _tcpServer?.CloseSessionsIdleLongerThan(GamePeekConstants.IdleDisconnectSeconds);
            }
        }

        // ── Event handlers (may arrive on background threads) ─────────────────

        private void OnClientConnected(int sessionId, string remoteIp)
            => Enqueue(() =>
            {
                var info = new DeviceInfo
                {
                    SessionId   = sessionId,
                    DeviceName  = "Unknown", // real name arrives with HELLO
                    IPAddress   = remoteIp ?? string.Empty,
                    ConnectedAt = DateTime.UtcNow,
                };
                _devices.Add(info);
                GamePeekConstants.Log($"[TCP] Device connected from {remoteIp} (session {sessionId})");

#if USE_INPUT_SYSTEM
                InputInjector.EnsureVirtualDevices();
#endif
                SetState(ConnectionState.Connected);
                DeviceConnected?.Invoke(info);
            });

        private void OnClientDisconnected(int sessionId)
            => Enqueue(() =>
            {
                // Session over (§3): stop sending UDP to that peer immediately.
                _udpSender?.RemoveSession(sessionId);

                int idx = _devices.FindIndex(d => d.SessionId == sessionId);
                if (idx < 0) return;

                var info = _devices[idx];
                _devices.RemoveAt(idx);
                GamePeekConstants.Log($"[TCP] Device disconnected: {info.DeviceName}");

                if (_hostSessionId == sessionId)
                {
                    _hostSessionId = _devices.Count > 0 ? _devices[0].SessionId : NoSession;
                    if (_hostSessionId != NoSession)
                        GamePeekConstants.Log($"[Auth] Host transferred to {_devices[0].DeviceName}");

                    // The departed host's STATS drove adaptation — restart from
                    // the configured ceilings so the next session doesn't
                    // inherit a degraded quality/fps/pacing state.
                    _adaptation?.OnConfigApplied(Config.Quality, Config.FpsCap);
                    _encoder?.SetQuality(Config.Quality);
                    _capture?.SetFpsCap(Config.FpsCap);
                }

                SetState(_devices.Count > 0 ? ConnectionState.Connected : ConnectionState.Advertising);
                DeviceDisconnected?.Invoke(info);
            });

        /// <summary>
        /// Raised on a TCP receive thread for every framed message (PINGs are
        /// answered inside the server). Binary SENSOR payloads are decoded here
        /// — no JsonUtility involved — and JSON payloads are marshalled raw to
        /// the main thread, where JsonUtility is safe. The queue preserves
        /// per-session message ordering.
        /// </summary>
        private void OnTcpMessage(int sessionId, byte msgType, byte[] payload)
        {
            switch (msgType)
            {
                // §5.1 body over TCP (0x0C) — the fallback-mode sensor path.
                // Accepted at any time regardless of the session's transport
                // (the phone infers its mode from where video arrives).
                case MsgType.Sensor:
                    if (payload.Length < Wire.SensorBodySize) return;
                    // Tolerate longer bodies for forward-compat, but say so
                    // once per session — silent tolerance hides size drift.
                    if (payload.Length != Wire.SensorBodySize && !_sensorSizeWarned)
                    {
                        _sensorSizeWarned = true;
                        GamePeekConstants.LogWarning(
                            $"[TCP] SENSOR payload is {payload.Length} bytes (expected {Wire.SensorBodySize}) — " +
                            "accepting the §5.1 prefix, but the sender is off-spec.");
                    }
                    byte  sensorType = payload[0];
                    float x = Wire.ReadF32(payload, 1);
                    float y = Wire.ReadF32(payload, 5);
                    float z = Wire.ReadF32(payload, 9);
                    Enqueue(() => HandleSensor(sessionId, sensorType, x, y, z));
                    return;

                case MsgType.Hello:
                case MsgType.Config:
                case MsgType.Touch:
                case MsgType.Stats:
                    string json;
                    try { json = Encoding.UTF8.GetString(payload); }
                    catch { return; }
                    Enqueue(() => DispatchJsonMessage(sessionId, msgType, json));
                    return;

                default:
                    // Unknown msgType: payload already consumed — ignoring it
                    // implements the spec's skip-and-continue rule (§4).
                    return;
            }
        }

        /// <summary>SENSOR datagram over UDP (0x03). Raised on the UDP receive thread.</summary>
        private void OnUdpSensorReceived(int sessionId, byte sensorType, float x, float y, float z)
            => Enqueue(() => HandleSensor(sessionId, sensorType, x, y, z));

        /// <summary>A session's video transport changed. May fire on any thread.</summary>
        private void OnTransportChanged(int sessionId, VideoTransport transport)
        {
            // Log helpers are thread-safe; no marshalling needed.
            if (transport == VideoTransport.Tcp)
            {
#if UNITY_EDITOR_WIN
                GamePeekConstants.LogWarning(
                    $"[UDP] Session {sessionId}: no PUNCH within {GamePeekConstants.PunchTimeoutSeconds:F0} s — " +
                    "falling back to video-over-TCP (higher latency). If this is unexpected, check that the " +
                    "GamePeek Windows firewall rule exists (GamePeek window ▸ Reset FW) — blocked UDP looks exactly like this.");
#else
                GamePeekConstants.LogWarning(
                    $"[UDP] Session {sessionId}: no PUNCH within {GamePeekConstants.PunchTimeoutSeconds:F0} s — " +
                    "falling back to video-over-TCP (higher latency). The network may block peer-to-peer UDP.");
#endif
            }
            else if (transport == VideoTransport.Udp)
            {
                GamePeekConstants.Log($"[UDP] Session {sessionId}: PUNCH received — video over UDP.");
            }
        }

        /// <summary>
        /// Parses a raw JSON payload and routes it to the typed handler.
        /// Runs on the main thread via the update-queue drain (JsonUtility is
        /// main-thread-only).
        /// </summary>
        private void DispatchJsonMessage(int sessionId, byte msgType, string json)
        {
            try
            {
                switch (msgType)
                {
                    case MsgType.Hello:
                        OnHelloReceived(sessionId, JsonUtility.FromJson<HelloMessage>(json));
                        break;

                    case MsgType.Config:
                        OnConfigReceived(sessionId, JsonUtility.FromJson<ConfigMessage>(json));
                        break;

                    // Only the host session may inject input, and only while
                    // CONFIG hasn't disabled touch.
                    case MsgType.Touch:
                        if (sessionId != _hostSessionId || !Config.TouchEnabled) break;
                        HandleTouch(JsonUtility.FromJson<TouchMessage>(json));
                        break;

                    case MsgType.Stats:
                        OnStatsReceived(sessionId, JsonUtility.FromJson<StatsMessage>(json));
                        break;
                }
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[TCP] Failed to parse message 0x{msgType:X2}: {ex.Message}\n{json}");
            }
        }

        /// <summary>
        /// STATS from the phone (§4.5, 1 Hz). The host's <c>rttMs</c> feeds the
        /// window's RTT display; the delivery/abandonment fields drive the
        /// adaptive pacing/quality/fps controller.
        /// </summary>
        private void OnStatsReceived(int sessionId, StatsMessage stats)
        {
            if (stats == null || sessionId != _hostSessionId) return;

            if (!Mathf.Approximately(SmoothedRtt, stats.rttMs))
            {
                SmoothedRtt = stats.rttMs;
                RttUpdated?.Invoke(SmoothedRtt);
            }

            _adaptation?.OnStats(stats);
        }

        /// <summary>
        /// Handles a config message. Runs on the main thread (via the update
        /// drain), so any preceding HELLO has already set the host session.
        /// </summary>
        private void OnConfigReceived(int sessionId, ConfigMessage msg)
        {
            if (msg == null) return;

            if (sessionId != _hostSessionId)
            {
                GamePeekConstants.LogWarning($"[Auth] Config rejected from non-host session {sessionId}");
                return;
            }

            int width  = Config.Width;
            int height = Config.Height;
            if (!string.IsNullOrEmpty(msg.resolution))
            {
                var parts = msg.resolution.Split('x');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int w) &&
                    int.TryParse(parts[1], out int h))
                {
                    width  = w;
                    height = h;
                }
            }

            // Resolution is always sent in portrait order (short × long).
            // Swap when the device is landscape so the capture RT matches the screen.
            if (msg.landscape && width < height) { int t = width; width = height; height = t; }
            else if (!msg.landscape && width > height) { int t = width; width = height; height = t; }

            int quality = msg.quality > 0 ? Mathf.Clamp(msg.quality, 1, 100) : Config.Quality;
            int fps     = msg.fps     > 0 ? Mathf.Clamp(msg.fps, 1, 120)     : Config.FpsCap;

            // Input-enable gates (§4.3). The POCO's field initializers keep
            // them enabled when a CONFIG omits them.
            Config.TouchEnabled = msg.touchEnabled;
            Config.GyroEnabled  = msg.gyroEnabled;
            Config.AccelEnabled = msg.accelEnabled;

            ApplyConfig(width, height, quality, fps);
        }

        /// <summary>
        /// Removes lingering <see cref="_devices"/> entries left behind by an
        /// abrupt drop (Wi-Fi off, app killed) when the same device reconnects.
        /// The liveness sweep only notices a dead peer after
        /// <see cref="GamePeekConstants.IdleDisconnectSeconds"/>, so until then
        /// the dead session would keep its slot — counting against the
        /// multi-device Pro gate and potentially holding the host role. A HELLO
        /// whose device name matches another session's entry can only be that
        /// device reconnecting, so the stale entry is evicted and its socket
        /// closed (PROTOCOL.md §3 keeps this v1 behavior).
        /// </summary>
        private void EvictStaleSessions(int sessionId, string deviceName)
        {
            // "Unknown" is the pre-HELLO placeholder — too ambiguous to treat
            // two sessions carrying it as the same physical device.
            if (string.IsNullOrEmpty(deviceName) || deviceName == "Unknown") return;

            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                var stale = _devices[i];
                if (stale.SessionId == sessionId || stale.DeviceName != deviceName) continue;

                _devices.RemoveAt(i);
                _udpSender?.RemoveSession(stale.SessionId);
                GamePeekConstants.Log($"[TCP] Evicted stale session {stale.SessionId} — {stale.DeviceName} reconnected as session {sessionId}");

                if (_hostSessionId == stale.SessionId)
                {
                    _hostSessionId = _devices.Count > 0 ? _devices[0].SessionId : NoSession;
                    if (_hostSessionId != NoSession)
                        GamePeekConstants.Log($"[Auth] Host transferred to {_devices[0].DeviceName}");
                }

                // Usually a no-op (the connection is already dead) but ends the
                // session cleanly if it is somehow still alive. Deferred a tick
                // (same pattern as the delayed closes in OnHelloReceived) so a
                // dead peer's close timeout cannot stall this HELLO.
                var staleServer  = _tcpServer;
                var staleSession = stale.SessionId;
                EditorApplication.delayCall += () => staleServer?.CloseSession(staleSession);

                DeviceDisconnected?.Invoke(stale);
            }
        }

        /// <summary>
        /// Handles the HELLO handshake (PROTOCOL.md §3/§4.1). Runs on the main
        /// thread (via the update drain).
        /// </summary>
        private void OnHelloReceived(int sessionId, HelloMessage hello)
        {
            if (hello == null) return;

            // ── Version gate ──────────────────────────────────────────────────
            if (hello.proto != GamePeekConstants.ProtoVersion)
            {
                GamePeekConstants.Log($"[Auth] Session {sessionId} rejected — protocol v{hello.proto} (editor speaks v{GamePeekConstants.ProtoVersion}).");
                SendErrorAndClose(sessionId, ErrorCodes.VersionMismatch,
                    $"This editor speaks GamePeek protocol v{GamePeekConstants.ProtoVersion}; the app sent v{hello.proto}. Update the app and the Unity plugin together.");
                return;
            }

            // Evict any half-open session this device left behind on an abrupt
            // drop BEFORE gating, so a zombie entry cannot occupy the free slot.
            // The name falls back to the connect-time entry when the HELLO
            // somehow omits one (it is required in v2).
            string deviceName = hello.deviceName;
            if (string.IsNullOrEmpty(deviceName))
            {
                int connectIdx = _devices.FindIndex(d => d.SessionId == sessionId);
                if (connectIdx >= 0) deviceName = _devices[connectIdx].DeviceName;
            }
            EvictStaleSessions(sessionId, deviceName);

            // Index of this session in _devices (recomputed after eviction). The
            // connect event is enqueued before any message from the same session,
            // so the device is normally already in the list.
            int deviceIdx = _devices.FindIndex(d => d.SessionId == sessionId);

            // ── Multi-device Pro gate ─────────────────────────────────────────
            // Streaming to more than one concurrent device is a Pro feature. The
            // first device is never gated; every additional one must claim Pro
            // in its HELLO. "Additional" means another session has already
            // completed a HELLO — not raw connect order — so a lingering zombie
            // session or a connection that never helloed cannot push the only
            // real device out of the free slot. This is honor-system — the tier
            // is client-asserted — consistent with the rest of the tier model.
            if (hello.tier != "pro" &&
                _devices.Exists(d => d.SessionId != sessionId && d.HelloReceived))
            {
                GamePeekConstants.Log($"[Auth] {deviceName ?? sessionId.ToString()} rejected — multi-device streaming requires Pro.");
                SendErrorAndClose(sessionId, ErrorCodes.ProRequired,
                    "Multi-device streaming requires GamePeek Pro.");
                return;
            }

            // First device to complete HELLO becomes the host (controls config and input).
            if (_hostSessionId == NoSession)
            {
                _hostSessionId = sessionId;
                GamePeekConstants.Log($"[Auth] Host session set: {deviceName ?? sessionId.ToString()}");
            }

            // Refresh the session's DeviceInfo from the HELLO payload. IsPro is
            // applied unconditionally — a Pro device that sends no deviceName must
            // still be recognised as Pro. HelloReceived marks the session as a
            // real GamePeek client for the Pro gate above.
            if (deviceIdx >= 0)
            {
                var old = _devices[deviceIdx];
                _devices[deviceIdx] = new DeviceInfo
                {
                    SessionId     = old.SessionId,
                    DeviceName    = string.IsNullOrEmpty(hello.deviceName) ? old.DeviceName : hello.deviceName,
                    IPAddress     = old.IPAddress,
                    ConnectedAt   = old.ConnectedAt,
                    IsPro         = hello.tier == "pro",
                    HelloReceived = true,
                };
                DeviceConnected?.Invoke(_devices[deviceIdx]);
            }

            GamePeekConstants.Log($"[TCP] Hello from {hello.client ?? "unknown"} ({deviceName ?? "?"}) session {sessionId}");

            // ── WELCOME (§4.2) ────────────────────────────────────────────────
            // Register the peer slot first: the §3 punch deadline starts with
            // the WELCOME, and a PUNCH may race in right after the send.
            if (_udpSender == null) return; // torn down between enqueue and drain
            uint token = Wire.GenerateToken(_udpSender.IsTokenTaken);
            _udpSender.RegisterSession(sessionId, token);

            string rawName = EditorPrefs.GetString(GamePeekConstants.PrefEditorName, string.Empty);
            SendJson(sessionId, MsgType.Welcome, new WelcomeMessage
            {
                proto       = GamePeekConstants.ProtoVersion,
                editorName  = string.IsNullOrWhiteSpace(rawName) ? Environment.MachineName : rawName,
                projectName = Application.productName,
                udpPort     = _udpSender.BoundPort,
                token       = token,
                codecs      = new int[] { VideoCodec.Jpeg },
                sliceCount  = FrameEncoder.SliceCount,
            });

            // Initial play-mode state, always, right after WELCOME (§4).
            SendJson(sessionId, MsgType.PlayMode,
                new PlayModeMessage { playing = Application.isPlaying });
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Fire only after the transition is complete so Application.isPlaying is correct.
            if (change != PlayModeStateChange.EnteredPlayMode &&
                change != PlayModeStateChange.EnteredEditMode) return;

            BroadcastPlayMode();
        }

        private void BroadcastPlayMode()
        {
            var payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                new PlayModeMessage { playing = Application.isPlaying }));
            _tcpServer?.Broadcast(MsgType.PlayMode, payload);
        }

        private void HandleTouch(TouchMessage msg)
        {
            if (msg == null) return;

            // Keep the Game View focused so the Input System processes injected events.
            // Focus only on "began" — doing it for every message (dozens per second
            // during a drag) repeatedly steals focus from whatever the user is editing.
            if (Application.isPlaying)
            {
                _gameViewFocusWarningLogged = false; // reset when we enter Play Mode
                if (msg.phase == "began")
                    GameViewSize.GetMainGameView()?.Focus();
            }
            else if (!_gameViewFocusWarningLogged)
            {
                _gameViewFocusWarningLogged = true;
                GamePeekConstants.LogWarning("[Input] Touch received but the Editor is not in Play Mode — Input System events will not be processed. Enter Play Mode or click the Game View to enable input.");
            }

            GamePeekConstants.Log($"[Input] Touch phase={msg.phase} x={msg.x:F3} y={msg.y:F3} finger={msg.fingerId}");
            InputInjector.InjectTouch(msg.phase, msg.x, msg.y, msg.fingerId);
            var pos = new Vector2(msg.x, msg.y);
            GamePeekInput.OnTouch?.Invoke(pos);
            GamePeekInput.OnTouchDetailed?.Invoke(msg.fingerId, msg.phase, pos);
        }

        /// <summary>
        /// Routes one sensor reading (§5.1) to the injector. Runs on the main
        /// thread via the update drain — sensors arrive over UDP (0x03) or TCP
        /// (0x0C) alike and are accepted regardless of the session's video
        /// transport; only the host's readings are honored, per-type CONFIG
        /// gates apply.
        /// </summary>
        private void HandleSensor(int sessionId, byte sensorType, float x, float y, float z)
        {
            if (sessionId != _hostSessionId) return;

            switch (sensorType)
            {
                case 1: // gyro, rad/s
                    if (!Config.GyroEnabled) return;
                    GamePeekConstants.Log($"[Input] Gyro  x={x:F3} y={y:F3} z={z:F3}");
                    InputInjector.InjectGyro(x, y, z);
                    break;

                case 2: // accel, m/s² (the injector converts to g-multiples)
                    if (!Config.AccelEnabled) return;
                    GamePeekConstants.Log($"[Input] Accel x={x:F3} y={y:F3} z={z:F3}");
                    InputInjector.InjectAccelerometer(x, y, z);
                    break;

                // 3 = attitude: reserved (§5.1) — ignore until specced.
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Serializes <paramref name="payload"/> with JsonUtility (main thread only) and sends it.</summary>
        private void SendJson(int sessionId, byte msgType, object payload)
            => _tcpServer?.Send(sessionId, msgType,
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));

        /// <summary>
        /// Sends an ERROR (§4.6) and closes the session on the next editor tick
        /// so the message flushes first. ERROR + close is terminal — the app
        /// must not auto-reconnect.
        /// </summary>
        private void SendErrorAndClose(int sessionId, string code, string message)
        {
            SendJson(sessionId, MsgType.Error, new ErrorMessage { code = code, message = message });
            var server = _tcpServer;
            EditorApplication.delayCall += () => server?.CloseSession(sessionId);
        }

        private void SetState(ConnectionState newState)
        {
            if (State == newState) return;
            State = newState;
            StateChanged?.Invoke(newState);
        }

        /// <summary>Enqueues an action to be executed on the next main-thread update.</summary>
        private void Enqueue(Action action) => _mainThreadQueue.Enqueue(action);
    }
}
