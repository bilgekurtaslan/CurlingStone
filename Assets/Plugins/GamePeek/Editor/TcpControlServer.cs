using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;

namespace GamePeek
{
    /// <summary>
    /// Protocol v2 control channel (PROTOCOL.md §4): a raw TCP server framing
    /// every message as <c>msgType (u8) + payloadLen (u32 LE) + payload</c>.
    /// <para>
    /// <b>Threading model:</b>
    /// <list type="bullet">
    ///   <item>One accept thread ("GamePeek TCP Accept") blocks on
    ///         <see cref="TcpListener.AcceptTcpClient"/>.</item>
    ///   <item>One receive thread per session parses framing only. RTT PINGs
    ///         (0x07) are answered right there with a binary PONG so the
    ///         phone's latency measurement never includes the editor
    ///         main-thread queue; every other message is surfaced raw via
    ///         <see cref="MessageReceived"/>.</item>
    ///   <item>All events are raised on socket threads. <c>JsonUtility</c> is a
    ///         main-thread-only API, so subscribers must marshal JSON payloads
    ///         to the Unity main thread before parsing (see
    ///         <see cref="ConnectionManager"/>). <see cref="ClientConnected"/>
    ///         is raised before the session's receive thread starts, so a
    ///         subscriber funnelling both into one queue sees the connect
    ///         ahead of the session's first message.</item>
    ///   <item><see cref="Send"/>/<see cref="Broadcast"/> are callable from any
    ///         thread; a per-session write lock serializes stream writes (the
    ///         video sender thread and the main thread both send here).</item>
    /// </list>
    /// </para>
    /// <para>
    /// Like <see cref="FrameEncoder"/>, the server self-registers on
    /// <see cref="AssemblyReloadEvents.beforeAssemblyReload"/> as a
    /// belt-and-braces guarantee that its threads never outlive the domain,
    /// even if <see cref="ConnectionManager"/>'s teardown is bypassed.
    /// </para>
    /// </summary>
    public sealed class TcpControlServer : IDisposable
    {
        // ── Per-connection state ──────────────────────────────────────────────

        private sealed class Session
        {
            public int           Id;
            public TcpClient     Client;
            public NetworkStream Stream;
            public string        RemoteIp;
            public Thread        ReceiveThread;

            /// <summary>Serializes writes: main thread, video sender thread.</summary>
            public readonly object WriteLock = new object();

            /// <summary>Scratch for the 5-byte frame header (guarded by <see cref="WriteLock"/>).</summary>
            public readonly byte[] HeaderScratch = new byte[Wire.TcpHeaderSize];

            /// <summary>UTC ticks of the last received byte (liveness, §3).</summary>
            public long LastRxTicks;

            /// <summary>0 = open, 1 = closed. Interlocked so racing closers act once.</summary>
            public int ClosedFlag;

            public bool Closed => Volatile.Read(ref ClosedFlag) != 0;
        }

        // ── Events (raised on socket threads) ─────────────────────────────────

        /// <summary>New TCP connection accepted. Args: (sessionId, remoteIp).</summary>
        public event Action<int, string> ClientConnected;

        /// <summary>Session ended (peer close, error, idle drop, or editor close). Args: (sessionId).</summary>
        public event Action<int> ClientDisconnected;

        /// <summary>
        /// One framed message, except PINGs (answered on the socket thread).
        /// Args: (sessionId, msgType, payload). Unknown msgTypes are surfaced
        /// too — ignoring them at the subscriber implements the spec's
        /// skip-and-continue rule.
        /// </summary>
        public event Action<int, byte, byte[]> MessageReceived;

        // ── State ─────────────────────────────────────────────────────────────

        private TcpListener   _listener;
        private Thread        _acceptThread;
        private volatile bool _running;
        private int           _nextSessionId;

        private readonly int _port;
        private readonly ConcurrentDictionary<int, Session> _sessions = new();

        /// <summary>Creates the server. Call <see cref="Start"/> to bind the socket.</summary>
        /// <param name="port">TCP port to listen on.</param>
        public TcpControlServer(int port) => _port = port;

        /// <summary>The port this server listens on (fixed at construction).</summary>
        public int Port => _port;

        /// <summary>Number of currently connected sessions.</summary>
        public int SessionCount => _sessions.Count;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Binds the listener and starts the accept thread. Throws on bind
        /// conflict with nothing left running, so callers can retry on the
        /// next port (see <see cref="ConnectionManager.StartStreaming"/>).
        /// </summary>
        public void Start()
        {
            if (_running) return;

            var listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();
            _listener = listener;
            _running  = true;

            // A leaked accept/receive thread must never outlive the domain.
            // Re-armed with the server; Stop() removes it.
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;

            _acceptThread = new Thread(AcceptLoop)
            {
                Name         = "GamePeek TCP Accept",
                IsBackground = true,
            };
            _acceptThread.Start();

            GamePeekConstants.Log($"[TCP] Control server listening on port {_port}.");
        }

        /// <summary>
        /// Stops the listener and closes every session <em>without</em>
        /// disconnect events (mirrors v1: teardown suppresses callbacks so a
        /// late event cannot land after the owner has already cleaned up).
        /// Safe to call multiple times and from the reload hook.
        /// </summary>
        public void Stop()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            if (!_running && _listener == null) return;
            _running = false;

            try { _listener?.Stop(); } catch { /* already down */ }
            _listener = null;

            foreach (var session in _sessions.Values)
                CloseSessionInternal(session, notify: false);
            _sessions.Clear();

            // Bounded joins: closing the listener/sockets unblocks the loops
            // immediately, so these normally return in microseconds. Background
            // threads cannot keep the editor alive either way.
            if (_acceptThread != null && !_acceptThread.Join(1000))
                GamePeekConstants.LogWarning("[TCP] Accept thread did not stop within 1 s.");
            _acceptThread = null;

            GamePeekConstants.Log("[TCP] Control server stopped.");
        }

        /// <inheritdoc/>
        public void Dispose() => Stop();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Sends one framed message to a session. Safe from any thread.
        /// Returns <c>false</c> when the session is gone (the send is dropped).
        /// </summary>
        /// <param name="payload">Payload bytes; null/empty for empty-payload messages.</param>
        public bool Send(int sessionId, byte msgType, byte[] payload = null)
            => Send(sessionId, msgType, payload, 0, payload?.Length ?? 0);

        /// <summary>
        /// Sends one framed message whose payload is a slice of
        /// <paramref name="payload"/>. Used by the video path to frame a chunk
        /// without copying it into an intermediate array.
        /// </summary>
        public bool Send(int sessionId, byte msgType, byte[] payload, int offset, int count)
        {
            if (!_running || !_sessions.TryGetValue(sessionId, out var session)) return false;
            return WriteFrame(session, msgType, null, 0, payload, offset, count);
        }

        /// <summary>
        /// Sends one framed message assembled from two segments written
        /// back-to-back: <paramref name="prefix"/> then a slice of
        /// <paramref name="payload"/>. The video-over-TCP fallback uses this to
        /// send the 19-byte §6 body header followed by the encoded slice with
        /// zero intermediate copies.
        /// </summary>
        public bool SendWithPrefix(int sessionId, byte msgType,
            byte[] prefix, int prefixLen, byte[] payload, int offset, int count)
        {
            if (!_running || !_sessions.TryGetValue(sessionId, out var session)) return false;
            return WriteFrame(session, msgType, prefix, prefixLen, payload, offset, count);
        }

        /// <summary>Sends one framed message to every connected session.</summary>
        public void Broadcast(byte msgType, byte[] payload = null)
        {
            if (!_running) return;
            foreach (var session in _sessions.Values)
                WriteFrame(session, msgType, null, 0, payload, 0, payload?.Length ?? 0);
        }

        /// <summary>
        /// Closes a session and raises <see cref="ClientDisconnected"/> so the
        /// owner's bookkeeping sees the close like any other disconnect.
        /// </summary>
        public void CloseSession(int sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                CloseSessionInternal(session, notify: true);
        }

        /// <summary>Remote IP of a session (empty string when unknown/gone).</summary>
        public string GetRemoteIp(int sessionId)
            => _sessions.TryGetValue(sessionId, out var s) ? s.RemoteIp : string.Empty;

        /// <summary>
        /// Drops every session that has sent nothing for
        /// <paramref name="idleSeconds"/> (liveness, PROTOCOL.md §3: the phone
        /// PINGs every 2 s, so 10 s of TCP silence means a dead peer). Called
        /// ~1 Hz from <see cref="ConnectionManager"/>'s update tick.
        /// </summary>
        public void CloseSessionsIdleLongerThan(double idleSeconds)
        {
            if (!_running) return;
            long now      = DateTime.UtcNow.Ticks;
            long maxTicks = (long)(idleSeconds * TimeSpan.TicksPerSecond);

            foreach (var session in _sessions.Values)
            {
                if (now - Volatile.Read(ref session.LastRxTicks) <= maxTicks) continue;
                GamePeekConstants.Log(
                    $"[TCP] Session {session.Id} dropped — no traffic for {idleSeconds:F0} s (dead peer).");
                CloseSessionInternal(session, notify: true);
            }
        }

        // ── Accept loop ───────────────────────────────────────────────────────

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    break; // listener stopped (teardown) — exit quietly
                }

                if (!_running)
                {
                    try { client.Close(); } catch { }
                    break;
                }

                try
                {
                    client.NoDelay = true;      // §4: TCP_NODELAY both ends
                    client.SendTimeout = 5000;  // a stuck peer must not wedge senders

                    var session = new Session
                    {
                        Id          = Interlocked.Increment(ref _nextSessionId),
                        Client      = client,
                        Stream      = client.GetStream(),
                        RemoteIp    = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString()
                                      ?? string.Empty,
                        LastRxTicks = DateTime.UtcNow.Ticks,
                    };
                    _sessions[session.Id] = session;

                    // Raise the connect BEFORE the receive thread starts so a
                    // subscriber funnelling both events into one queue observes
                    // the connect ahead of the session's first message (HELLO).
                    ClientConnected?.Invoke(session.Id, session.RemoteIp);

                    session.ReceiveThread = new Thread(() => ReceiveLoop(session))
                    {
                        Name         = $"GamePeek TCP Session {session.Id}",
                        IsBackground = true,
                    };
                    session.ReceiveThread.Start();
                }
                catch (Exception ex)
                {
                    GamePeekConstants.LogWarning($"[TCP] Failed to set up connection: {ex.Message}");
                    try { client.Close(); } catch { }
                }
            }
        }

        // ── Per-session receive loop ──────────────────────────────────────────

        private void ReceiveLoop(Session session)
        {
            var header = new byte[Wire.TcpHeaderSize];
            try
            {
                while (_running && !session.Closed)
                {
                    if (!ReadExactly(session.Stream, header, header.Length)) break;
                    Volatile.Write(ref session.LastRxTicks, DateTime.UtcNow.Ticks);

                    byte msgType = header[0];
                    uint length  = Wire.ReadU32(header, 1);
                    if (length > Wire.MaxTcpPayload)
                    {
                        // §4: larger than the sanity bound = protocol error →
                        // close. Also what a v1 app's WebSocket HTTP upgrade
                        // request decodes to, so this is the v1-client bouncer.
                        GamePeekConstants.LogWarning(
                            $"[TCP] Session {session.Id}: invalid payload length {length} (protocol error or v1 client) — closing.");
                        break;
                    }

                    byte[] payload = length == 0 ? Array.Empty<byte>() : new byte[length];
                    if (length > 0 && !ReadExactly(session.Stream, payload, (int)length)) break;
                    Volatile.Write(ref session.LastRxTicks, DateTime.UtcNow.Ticks);

                    // RTT ping: echo right here on the socket thread (§4 0x08) so
                    // the phone's measurement excludes the main-thread queue.
                    if (msgType == MsgType.Ping)
                    {
                        WriteFrame(session, MsgType.Pong, null, 0, payload, 0, payload.Length);
                        continue;
                    }

                    MessageReceived?.Invoke(session.Id, msgType, payload);
                }
            }
            catch
            {
                // Socket torn down mid-read (disconnect, Stop, domain unload) —
                // fall through to the close below.
            }
            finally
            {
                CloseSessionInternal(session, notify: _running);
            }
        }

        /// <summary>Blocking read of exactly <paramref name="count"/> bytes; false on EOF.</summary>
        private static bool ReadExactly(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        // ── Write path ────────────────────────────────────────────────────────

        /// <summary>
        /// Writes one frame (header + optional prefix + payload slice) under the
        /// session's write lock. Any failure closes the session — half-written
        /// frames make the stream unparseable, so the connection cannot survive.
        /// </summary>
        private bool WriteFrame(Session session, byte msgType,
            byte[] prefix, int prefixLen, byte[] payload, int offset, int count)
        {
            if (session.Closed) return false;
            try
            {
                lock (session.WriteLock)
                {
                    var header = session.HeaderScratch;
                    header[0] = msgType;
                    Wire.WriteU32(header, 1, (uint)(prefixLen + count));
                    session.Stream.Write(header, 0, Wire.TcpHeaderSize);
                    if (prefixLen > 0) session.Stream.Write(prefix, 0, prefixLen);
                    if (count > 0)     session.Stream.Write(payload, offset, count);
                }
                return true;
            }
            catch (Exception ex)
            {
                GamePeekConstants.Log($"[TCP] Send to session {session.Id} failed ({ex.GetType().Name}) — closing.");
                CloseSessionInternal(session, notify: _running);
                return false;
            }
        }

        // ── Close ─────────────────────────────────────────────────────────────

        private void CloseSessionInternal(Session session, bool notify)
        {
            // Racing closers (receive loop, write failure, idle drop, Stop) —
            // exactly one proceeds.
            if (Interlocked.Exchange(ref session.ClosedFlag, 1) != 0) return;

            _sessions.TryRemove(session.Id, out _);
            try { session.Client.Close(); } catch { }

            if (notify) ClientDisconnected?.Invoke(session.Id);
        }
    }
}
