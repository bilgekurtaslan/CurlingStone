using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;

namespace GamePeek
{
    /// <summary>
    /// One encoded video frame ready for the wire: standalone JPEG slices in
    /// display order (top to bottom), per PROTOCOL.md §6. Produced by
    /// <see cref="FrameEncoder"/>, consumed by the sender thread. Slice arrays
    /// are treated as immutable once deposited (the encoder allocates fresh
    /// arrays per encode and never mutates old ones).
    /// </summary>
    public sealed class EncodedVideoFrame
    {
        public int      Width;
        public int      Height;
        public byte[][] Slices;
    }

    /// <summary>How video reaches a given session (PROTOCOL.md §3).</summary>
    public enum VideoTransport
    {
        /// <summary>WELCOME sent; waiting up to 2 s for the first UDP PUNCH.</summary>
        AwaitingPunch,
        /// <summary>UDP datagrams to the endpoint learned from the newest PUNCH.</summary>
        Udp,
        /// <summary>Video-over-TCP rescue mode (§7) — no PUNCH arrived, or UDP died.</summary>
        Tcp,
    }

    /// <summary>
    /// Owner of the protocol v2 UDP socket (PROTOCOL.md §5/§6): sends VIDEO
    /// datagrams to every ready peer and receives PUNCH/SENSOR datagrams.
    /// <para>
    /// One socket is shared by all peers (per-session <c>token</c>s map inbound
    /// datagrams to sessions). The socket binds the same port number as the
    /// bound TCP port when possible (§2) so the Windows firewall's port-range
    /// rule covers it; <c>WELCOME.udpPort</c> stays authoritative regardless.
    /// </para>
    /// <para>
    /// <b>Threading model:</b>
    /// <list type="bullet">
    ///   <item>One receive thread ("GamePeek UDP Receive") blocks on
    ///         <c>ReceiveFrom</c>. PUNCH updates the peer's endpoint and
    ///         (re)activates UDP transport — at any time, including switching
    ///         back from TCP fallback after a Wi-Fi roam (§3). SENSOR is
    ///         surfaced via <see cref="SensorReceived"/> directly on this
    ///         thread (<see cref="InputInjector"/> is thread-safe).</item>
    ///   <item>One sender thread ("GamePeek UDP Sender") drains a single-slot
    ///         latest-frame mailbox (§6.2): <see cref="SubmitEncodedFrame"/> is
    ///         non-blocking and a deposit while the sender is busy overwrites
    ///         the slot — superseded frames are never sent. Each frame fans out
    ///         per peer: UDP chunks of ≤ 1100 payload bytes, or one whole-slice
    ///         VIDEO_TCP message per slice for fallback peers (§7).</item>
    ///   <item>Peer registration/removal happens on the main thread
    ///         (<see cref="ConnectionManager"/>'s HELLO/disconnect handling);
    ///         the peer tables are concurrent dictionaries and per-peer state
    ///         is written with volatile/interlocked semantics.</item>
    /// </list>
    /// </para>
    /// Self-registers on <see cref="AssemblyReloadEvents.beforeAssemblyReload"/>
    /// so a leaked receive thread can never outlive the domain.
    /// </summary>
    public sealed class UdpVideoSender : IDisposable
    {
        // ── Per-peer state ────────────────────────────────────────────────────

        private sealed class Peer
        {
            public int  SessionId;
            public uint Token;

            /// <summary>(int)<see cref="VideoTransport"/> — interlocked transitions.</summary>
            public int TransportInt = (int)VideoTransport.AwaitingPunch;

            /// <summary>Endpoint learned from the newest PUNCH (null until the first).</summary>
            public EndPoint Endpoint;

            /// <summary>UTC ticks when WELCOME was sent (starts the §3 punch deadline).</summary>
            public long WelcomeTicks;
        }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// A session's video transport changed. Args: (sessionId, transport).
        /// Raised on the UDP receive thread (PUNCH arrival) or on whichever
        /// thread ran the punch-deadline check — marshal before touching Unity
        /// APIs other than the thread-safe log helpers.
        /// </summary>
        public event Action<int, VideoTransport> TransportChanged;

        /// <summary>
        /// SENSOR datagram received (§5.1). Args: (sessionId, sensorType, x, y, z).
        /// Raised on the UDP receive thread.
        /// </summary>
        public event Action<int, byte, float, float, float> SensorReceived;

        // ── State ─────────────────────────────────────────────────────────────

        private Socket        _socket;
        private Thread        _receiveThread;
        private Thread        _senderThread;
        private volatile bool _running;

        // ── Latest-frame mailbox (producer: encoder workers; consumer: sender) ─
        private readonly object _mailboxLock = new object();
        private EncodedVideoFrame _mailbox;
        private AutoResetEvent    _frameReady;
        private int _supersededFrames;

        /// <summary>Frames that were overwritten in the mailbox before the
        /// sender could ship them (§6.2 — superseded frames are never sent).</summary>
        public int SupersededFrames => _supersededFrames;

        // Bumped whenever a peer becomes able to receive video (PUNCH
        // activation or TCP-fallback flip). The encoder compares this across
        // frames to defeat its skip-unchanged optimisation exactly once, so a
        // freshly connected phone still receives a static scene's frame.
        private int _peerGeneration;

        /// <summary>Changes whenever a peer becomes ready to receive video.</summary>
        public int PeerGeneration => Volatile.Read(ref _peerGeneration);

        private readonly ConcurrentDictionary<uint, Peer> _peersByToken   = new();
        private readonly ConcurrentDictionary<int, Peer>  _peersBySession = new();

        /// <summary>Control server used for VIDEO_TCP fallback sends (§7).</summary>
        private TcpControlServer _tcpServer;

        // Shared across all sessions — a session's first observed frameId is
        // arbitrary per §6; only per-session monotonicity matters.
        // Sender-thread only.
        private uint _frameId;

        // Total frames put on the wire (any transport). Written by the sender
        // thread, read by the adaptation controller to compute the true send
        // rate — capture fps overcounts whenever skip-unchanged is active.
        private long _framesSent;

        /// <summary>Total frames sent since Start (monotonic counter).</summary>
        public long TotalFramesSent => Interlocked.Read(ref _framesSent);

        // Sender-thread scratch: one datagram (24-byte header + max chunk) and
        // one 19-byte VIDEO_TCP body header, reused across chunks and peers.
        private readonly byte[] _datagramScratch = new byte[Wire.VideoUdpHeaderSize + Wire.MaxChunkPayload];
        private readonly byte[] _tcpHeaderScratch = new byte[Wire.VideoBodyHeaderSize];

        /// <summary>
        /// Microseconds to sleep between chunk sends — phase 3 adaptation hook
        /// (PROTOCOL.md §6.2). 0 = no pacing. Written on the main thread, read
        /// on the sender thread.
        /// </summary>
        public volatile int PaceDelayMicros;

        /// <summary>The UDP port actually bound (authoritative value for WELCOME).</summary>
        public int BoundPort { get; private set; }

        // One warning per streaming session when a peer's SENSOR body size
        // drifts off the §5.1 spec (the instance is recreated per session).
        private bool _sensorSizeWarned;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Binds the UDP socket and starts the receive thread.
        /// Tries <paramref name="preferredPort"/>…+9 (the bound TCP port —
        /// keeps the firewall's port-range rule valid), then falls back to an
        /// OS-assigned port with a loud warning (§2).
        /// </summary>
        /// <param name="tcpServer">Control server used for VIDEO_TCP fallback sends.</param>
        public void Start(int preferredPort, TcpControlServer tcpServer)
        {
            if (_running) return;
            _tcpServer = tcpServer;

            _socket   = BindSocket(preferredPort, out int boundPort);
            BoundPort = boundPort;

            if (boundPort < preferredPort || boundPort > preferredPort + GamePeekConstants.PortBindAttempts - 1)
            {
#if UNITY_EDITOR_WIN
                // The firewall helper's UDP rule covers [port, port+9] only. An
                // OS-assigned port outside it means inbound PUNCH may be
                // silently blocked → sessions land in TCP fallback and look
                // like "laggy video". Say so loudly.
                GamePeekConstants.LogError(
                    $"[UDP] Ports {preferredPort}–{preferredPort + GamePeekConstants.PortBindAttempts - 1} were all taken; " +
                    $"bound OS-assigned UDP port {boundPort} instead. The Windows firewall rule does NOT cover this port — " +
                    "if video falls back to TCP (higher latency), free the preferred ports or add a manual UDP allow rule.");
#else
                GamePeekConstants.LogWarning(
                    $"[UDP] Ports {preferredPort}–{preferredPort + GamePeekConstants.PortBindAttempts - 1} were all taken; " +
                    $"bound OS-assigned UDP port {boundPort} instead.");
#endif
            }

            _running = true;

            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;

            _receiveThread = new Thread(ReceiveLoop)
            {
                Name         = "GamePeek UDP Receive",
                IsBackground = true,
            };
            _receiveThread.Start();

            _frameReady ??= new AutoResetEvent(false);
            _senderThread = new Thread(SenderLoop)
            {
                Name         = "GamePeek UDP Sender",
                IsBackground = true,
            };
            _senderThread.Start();

            GamePeekConstants.Log($"[UDP] Video socket bound on port {boundPort}.");
        }

        /// <summary>Stops the receive and sender threads, closes the socket, and clears all peers.</summary>
        public void Stop()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            if (!_running && _socket == null) return;
            _running = false;

            try { _socket?.Close(); } catch { /* already down */ }
            _socket = null;
            _frameReady?.Set();

            // Shared join deadline across both threads — teardown (the domain
            // reload path in particular) must be bounded regardless of thread
            // count. Closing the socket unblocks ReceiveFrom immediately; the
            // event wake unblocks the sender.
            var  deadline  = System.Diagnostics.Stopwatch.StartNew();
            bool allJoined =
                (_receiveThread?.Join((int)Math.Max(0, 2000 - deadline.ElapsedMilliseconds)) ?? true) &
                (_senderThread?.Join((int)Math.Max(0, 2000 - deadline.ElapsedMilliseconds)) ?? true);
            if (!allJoined)
                GamePeekConstants.LogWarning("[UDP] Threads did not stop within 2 s.");
            _receiveThread = null;
            _senderThread  = null;

            // Only dispose the wait handle once the sender is provably gone.
            if (allJoined && _frameReady != null)
            {
                _frameReady.Dispose();
                _frameReady = null;
            }

            lock (_mailboxLock) _mailbox = null;
            _peersByToken.Clear();
            _peersBySession.Clear();
            _tcpServer = null;

            GamePeekConstants.Log("[UDP] Video socket closed.");
        }

        /// <inheritdoc/>
        public void Dispose() => Stop();

        /// <summary>
        /// Creates and binds the socket, retrying <c>preferredPort…+9</c> then
        /// falling back to OS-assigned. A fresh socket per attempt keeps
        /// failed-bind state off the one we keep.
        /// </summary>
        private static Socket BindSocket(int preferredPort, out int boundPort)
        {
            for (int attempt = 0; attempt <= GamePeekConstants.PortBindAttempts; attempt++)
            {
                int port = attempt < GamePeekConstants.PortBindAttempts
                    ? preferredPort + attempt
                    : 0; // last resort: OS-assigned
                if (port > 65535) continue;

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Any, port));
                }
                catch (SocketException)
                {
                    socket.Dispose();
                    continue;
                }

#if UNITY_EDITOR_WIN
                // §6.2: without this, one peer's ICMP port-unreachable poisons
                // the shared socket — every subsequent SendTo throws
                // WSAECONNRESET and video dies for ALL peers.
                try
                {
                    const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
                    socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
                }
                catch { /* best effort — very old Windows builds lack it */ }
#endif

                boundPort = ((IPEndPoint)socket.LocalEndPoint).Port;
                return socket;
            }

            throw new InvalidOperationException("Could not bind any UDP port.");
        }

        // ── Peer management (main thread) ─────────────────────────────────────

        /// <summary>
        /// Registers a session's peer slot right before its WELCOME is sent —
        /// the §3 punch deadline starts now. A repeated HELLO on the same
        /// session replaces the slot (and retires the old token) so stale
        /// datagrams cannot resurrect it.
        /// </summary>
        public void RegisterSession(int sessionId, uint token)
        {
            if (_peersBySession.TryRemove(sessionId, out var previous))
                _peersByToken.TryRemove(previous.Token, out _);

            var peer = new Peer
            {
                SessionId    = sessionId,
                Token        = token,
                WelcomeTicks = DateTime.UtcNow.Ticks,
            };
            _peersByToken[token]      = peer;
            _peersBySession[sessionId] = peer;
        }

        /// <summary>Removes a session's peer slot (TCP disconnect = session over, §3).</summary>
        public void RemoveSession(int sessionId)
        {
            if (_peersBySession.TryRemove(sessionId, out var peer))
                _peersByToken.TryRemove(peer.Token, out _);
        }

        /// <summary>Token collision check for <see cref="Wire.GenerateToken"/>.</summary>
        public bool IsTokenTaken(uint token) => _peersByToken.ContainsKey(token);

        /// <summary>A session's current video transport (TCP fallback when unknown).</summary>
        public VideoTransport GetTransport(int sessionId)
            => _peersBySession.TryGetValue(sessionId, out var peer)
                ? (VideoTransport)Volatile.Read(ref peer.TransportInt)
                : VideoTransport.Tcp;

        /// <summary>
        /// <c>true</c> when at least one peer can receive video right now
        /// (punched UDP or TCP fallback). The encoder uses this to skip
        /// capture work when nobody is watching — peers still inside the
        /// 2 s punch window don't count.
        /// </summary>
        public bool HasReadyPeers
        {
            get
            {
                foreach (var peer in _peersBySession.Values)
                    if (Volatile.Read(ref peer.TransportInt) != (int)VideoTransport.AwaitingPunch)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Flips peers whose 2 s punch window expired to TCP fallback (§3).
        /// Called every editor update tick from <see cref="ConnectionManager"/>;
        /// cheap — iterates only the handful of connected peers.
        /// </summary>
        public void ApplyPunchDeadlines()
        {
            long now      = DateTime.UtcNow.Ticks;
            long deadline = (long)(GamePeekConstants.PunchTimeoutSeconds * TimeSpan.TicksPerSecond);

            foreach (var peer in _peersBySession.Values)
            {
                if (Volatile.Read(ref peer.TransportInt) != (int)VideoTransport.AwaitingPunch) continue;
                if (now - peer.WelcomeTicks < deadline) continue;

                // CompareExchange: a PUNCH racing in right now wins — UDP it is.
                if (Interlocked.CompareExchange(ref peer.TransportInt,
                        (int)VideoTransport.Tcp, (int)VideoTransport.AwaitingPunch)
                    == (int)VideoTransport.AwaitingPunch)
                {
                    Interlocked.Increment(ref _peerGeneration);
                    TransportChanged?.Invoke(peer.SessionId, VideoTransport.Tcp);
                }
            }
        }

        // ── Receive thread ────────────────────────────────────────────────────

        private void ReceiveLoop()
        {
            var buffer = new byte[2048];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                int length;
                try
                {
                    length = _socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException)
                {
                    // WSAECONNRESET (unsuppressed platforms) or transient error —
                    // keep receiving unless we're shutting down.
                    if (!_running) break;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break; // Stop() closed the socket
                }

                if (length < Wire.UdpPrefixSize) continue;

                // §5: token mismatch = drop silently (stray/stale datagram).
                uint token = Wire.ReadU32(buffer, 0);
                if (!_peersByToken.TryGetValue(token, out var peer)) continue;

                switch (buffer[4])
                {
                    case PktType.Punch:
                        HandlePunch(peer, remote);
                        break;

                    case PktType.Sensor:
                        if (length < Wire.UdpPrefixSize + Wire.SensorBodySize) break;
                        // Tolerate longer bodies for forward-compat, but say so
                        // once — silent tolerance is how a serializer size
                        // drift survives everyone's tests (it did, once).
                        if (length != Wire.UdpPrefixSize + Wire.SensorBodySize && !_sensorSizeWarned)
                        {
                            _sensorSizeWarned = true;
                            GamePeekConstants.LogWarning(
                                $"[UDP] SENSOR datagram is {length} bytes (expected {Wire.UdpPrefixSize + Wire.SensorBodySize}) — " +
                                "accepting the §5.1 prefix, but the sender is off-spec.");
                        }
                        SensorReceived?.Invoke(peer.SessionId,
                            buffer[5],
                            Wire.ReadF32(buffer, 6),
                            Wire.ReadF32(buffer, 10),
                            Wire.ReadF32(buffer, 14));
                        break;

                    // PktType.Video/Audio are editor→phone only; anything else
                    // is a future packet type — ignore (forward compatibility).
                }
            }
        }

        /// <summary>
        /// PUNCH: adopt the newest source endpoint for this token and
        /// (re)activate UDP transport — §3 allows this at any time, including
        /// switching back from TCP fallback after a Wi-Fi roam.
        /// </summary>
        private void HandlePunch(Peer peer, EndPoint remote)
        {
            var incoming = (IPEndPoint)remote;
            var current  = (IPEndPoint)Volatile.Read(ref peer.Endpoint);
            if (current == null ||
                current.Port != incoming.Port ||
                !current.Address.Equals(incoming.Address))
            {
                // Copy — ReceiveFrom owns/replaces the `remote` instance.
                Volatile.Write(ref peer.Endpoint, new IPEndPoint(incoming.Address, incoming.Port));
            }

            int previous = Interlocked.Exchange(ref peer.TransportInt, (int)VideoTransport.Udp);
            if (previous != (int)VideoTransport.Udp)
            {
                Interlocked.Increment(ref _peerGeneration);
                TransportChanged?.Invoke(peer.SessionId, VideoTransport.Udp);
            }
        }

        // ── Send path (mailbox + sender thread) ───────────────────────────────

        /// <summary>
        /// Deposits one encoded frame into the latest-frame mailbox.
        /// Non-blocking and callable from any thread (the encoder workers call
        /// it); a frame already waiting in the slot is overwritten — superseded
        /// frames are never sent (§6.2).
        /// </summary>
        public void SubmitEncodedFrame(EncodedVideoFrame frame)
        {
            if (!_running || frame?.Slices == null || frame.Slices.Length == 0) return;
            if (frame.Width <= 0 || frame.Height <= 0 ||
                frame.Width > ushort.MaxValue || frame.Height > ushort.MaxValue) return;
            if (frame.Slices.Length > byte.MaxValue) return;

            lock (_mailboxLock)
            {
                if (_mailbox != null) _supersededFrames++;
                _mailbox = frame;
            }
            _frameReady?.Set();
        }

        /// <summary>
        /// Dedicated sender loop: waits for the mailbox signal, takes the
        /// latest frame, and fans it out to every ready peer. Exactly one
        /// frame is on the wire at a time; encode and send are decoupled.
        /// </summary>
        private void SenderLoop()
        {
            try
            {
                while (_running)
                {
                    _frameReady.WaitOne();
                    if (!_running) break;

                    EncodedVideoFrame frame;
                    lock (_mailboxLock)
                    {
                        frame    = _mailbox;
                        _mailbox = null;
                    }
                    if (frame == null) continue;

                    SendFrameToAllPeers(frame);
                }
            }
            catch
            {
                // WaitOne on a disposed handle after an unclean stop, or a
                // thread abort during domain unload — the loop simply ends.
            }
        }

        /// <summary>Fans one frame out: UDP chunks per slice, or whole-slice
        /// VIDEO_TCP messages for fallback peers (§7).</summary>
        private void SendFrameToAllPeers(EncodedVideoFrame frame)
        {
            uint frameId    = unchecked(_frameId++);
            byte sliceCount = (byte)frame.Slices.Length;
            Interlocked.Increment(ref _framesSent);

            foreach (var peer in _peersBySession.Values)
            {
                switch ((VideoTransport)Volatile.Read(ref peer.TransportInt))
                {
                    case VideoTransport.Udp:
                        for (byte si = 0; si < sliceCount; si++)
                        {
                            byte[] slice      = frame.Slices[si];
                            int    chunkCount = (slice.Length + Wire.MaxChunkPayload - 1) / Wire.MaxChunkPayload;
                            if (chunkCount == 0 || chunkCount > ushort.MaxValue) continue;
                            SendUdpSlice(peer, frameId, slice, slice.Length, chunkCount,
                                si, sliceCount, (ushort)frame.Width, (ushort)frame.Height);
                        }
                        break;

                    case VideoTransport.Tcp:
                        // §7: one chunk per slice — per-chunk framing buys
                        // nothing on a reliable stream.
                        for (byte si = 0; si < sliceCount; si++)
                        {
                            byte[] slice = frame.Slices[si];
                            Wire.WriteVideoBodyHeader(_tcpHeaderScratch, 0,
                                VideoCodec.Jpeg, frameId,
                                si, sliceCount,
                                chunkIndex: 0, chunkCount: 1,
                                (uint)slice.Length, (ushort)frame.Width, (ushort)frame.Height);
                            _tcpServer?.SendWithPrefix(peer.SessionId, MsgType.VideoTcp,
                                _tcpHeaderScratch, Wire.VideoBodyHeaderSize, slice, 0, slice.Length);
                        }
                        break;

                    // AwaitingPunch: not ready — the frame is simply not for them yet.
                }
            }
        }

        /// <summary>Sends one slice to one UDP peer as ≤ 1100-byte chunks.</summary>
        private void SendUdpSlice(Peer peer, uint frameId, byte[] slice, int sliceLen,
            int chunkCount, byte sliceIndex, byte sliceCount, ushort width, ushort height)
        {
            var endpoint = Volatile.Read(ref peer.Endpoint);
            if (endpoint == null) return;

            var socket = _socket;
            if (socket == null) return;

            Wire.WriteUdpPrefix(_datagramScratch, peer.Token, PktType.Video);

            int pace = PaceDelayMicros;
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int offset = chunkIndex * Wire.MaxChunkPayload;
                int count  = Math.Min(Wire.MaxChunkPayload, sliceLen - offset);

                Wire.WriteVideoBodyHeader(_datagramScratch, Wire.UdpPrefixSize,
                    VideoCodec.Jpeg, frameId, sliceIndex, sliceCount,
                    (ushort)chunkIndex, (ushort)chunkCount,
                    (uint)sliceLen, width, height);
                Buffer.BlockCopy(slice, offset, _datagramScratch, Wire.VideoUdpHeaderSize, count);

                try
                {
                    socket.SendTo(_datagramScratch, 0, Wire.VideoUdpHeaderSize + count,
                        SocketFlags.None, endpoint);
                }
                catch (SocketException)
                {
                    // Transient (buffer full, unreachable) — video is
                    // loss-tolerant and TCP liveness governs the session (§3).
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return; // torn down mid-frame
                }

                // Pacing hook (§6.2): spread the chunk burst so Wi-Fi AP
                // buffers don't tail-drop it. 0 (default) = full speed; phase 3
                // adaptation raises it when the phone reports abandoned frames.
                if (pace > 0 && chunkIndex + 1 < chunkCount)
                    PaceWait(pace);
            }
        }

        /// <summary>
        /// Waits approximately <paramref name="micros"/> microseconds on the
        /// sender thread. Sleep granularity is ~1 ms, so sub-millisecond waits
        /// spin on the monotonic clock instead (the sender thread has nothing
        /// else to do mid-frame, and typical values are tens of µs).
        /// </summary>
        private static void PaceWait(int micros)
        {
            if (micros >= 2000)
            {
                Thread.Sleep(micros / 1000);
                return;
            }
            long target = System.Diagnostics.Stopwatch.GetTimestamp()
                          + micros * System.Diagnostics.Stopwatch.Frequency / 1_000_000;
            while (System.Diagnostics.Stopwatch.GetTimestamp() < target)
                Thread.SpinWait(64);
        }
    }
}
