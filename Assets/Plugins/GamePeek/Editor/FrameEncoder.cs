using System;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GamePeek
{
    /// <summary>
    /// Encodes captured frames as JPEG off the Unity main thread — split into
    /// horizontal slices encoded by parallel workers (wire protocol v2,
    /// docs~/PROTOCOL.md §6) — and deposits the result into the
    /// <see cref="UdpVideoSender"/>'s latest-frame mailbox.
    /// <para>
    /// <b>Pipeline:</b> the main thread only memcpys each slice's raw RGB24
    /// rows into a per-worker buffer and wakes the workers. Each worker hashes
    /// its slice (FNV-1a 64) and encodes it with
    /// <c>ImageConversion.EncodeArrayToJPG</c> — or reuses its cached JPEG when
    /// the pixels did not change since the previous frame. The last worker to
    /// finish assembles the frame: if <em>every</em> slice is unchanged the
    /// frame is skipped entirely (a static scene sends nothing), otherwise the
    /// slices go to the sender mailbox. Encode and send are fully decoupled:
    /// <see cref="IsEncoding"/> only covers the encode stage, and a frame that
    /// is finished while the sender is still shipping the previous one simply
    /// overwrites the mailbox slot (superseded frames are never sent, §6.2).
    /// </para>
    /// <para>
    /// <b>Slice geometry:</b> slice <c>i</c> covers display rows
    /// <c>[i·H/N, (i+1)·H/N)</c> (integer floor division, §6). Every capture
    /// path delivers bottom-row-first RGB24, and <c>EncodeArrayToJPG</c>
    /// consumes bottom-first input, so display slice <c>i</c> is the contiguous
    /// buffer region starting at byte <c>(H − rowEnd_i)·W·3</c> — one memcpy
    /// per slice, no row reordering ever.
    /// </para>
    /// <para>
    /// <b>Threading model:</b>
    /// <list type="bullet">
    ///   <item><see cref="SubmitFrame(Texture2D)"/> and
    ///         <see cref="SubmitFrame(NativeArray{byte}, int, int)"/> run on the
    ///         Unity <em>main</em> thread and only copy raw bytes.</item>
    ///   <item>N encode workers ("GamePeek Encode i"),
    ///         N = min(<see cref="SliceCount"/>, cores − 2, frame height),
    ///         each own one slice per frame.</item>
    ///   <item>The network send runs on <see cref="UdpVideoSender"/>'s dedicated
    ///         sender thread, fed through the mailbox.</item>
    ///   <item>Safety valve: if any worker encode ever throws, the encoder
    ///         permanently reverts to synchronous single-slice main-thread
    ///         encoding for the rest of the session (per-packet sliceCount
    ///         makes the change transparent to the phone — WELCOME's value is
    ///         advisory).</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class FrameEncoder
    {
        /// <summary>
        /// Maximum horizontal slices per frame, advertised in WELCOME
        /// (advisory — every VIDEO packet carries its own authoritative
        /// value). The effective count is capped by core count and may drop
        /// to 1 after an encode failure.
        /// </summary>
        public const int SliceCount = 4;

        // ── Encode format ─────────────────────────────────────────────────────

        // Every capture path hands us display-ready gamma (sRGB-encoded) bytes:
        // in Linear-colour-space projects the sources are sRGB render textures
        // whose ReadPixels/AsyncGPUReadback yields the gamma bytes untouched, and
        // in Gamma projects everything is gamma bytes anyway. R8G8B8_SRGB declares
        // exactly that, so EncodeArrayToJPG writes the bytes into the JPEG
        // untouched. Declaring R8G8B8_UNorm instead would mark the data as
        // linear, and the encoder would apply a linear→sRGB transfer — a second
        // gamma pass that whitens the image.
        private const GraphicsFormat EncodeFormat = GraphicsFormat.R8G8B8_SRGB;

        // ── Pending frame (written by main thread while !_busy) ──────────────

        private struct PendingFrame
        {
            public int Width;
            public int Height;
            public int Quality;
            public int SlicesUsed;
        }

        private PendingFrame _pending;

        // ── Workers ───────────────────────────────────────────────────────────

        private sealed class Worker
        {
            public Thread         Thread;
            public AutoResetEvent Wake;

            /// <summary>Slice pixels for the current job. Written by the main
            /// thread only while <c>!_busy</c>; read by this worker only while
            /// <c>_busy</c> — no lock needed.</summary>
            public byte[] Raw;
            public int    Rows;

            /// <summary>Result of the current job (null = encode failed).</summary>
            public byte[] Jpeg;
            public bool   Unchanged;
            public float  EncodeMs;

            /// <summary>Skip-unchanged cache: hash + JPEG of the previous
            /// frame's slice content. Valid for the current geometry key only.</summary>
            public ulong  LastHash;
            public byte[] LastJpeg;
        }

        private Worker[]      _workers;
        private volatile bool _running;
        private int           _workersRemaining;  // interlocked countdown per frame
        private long          _geometryKey;       // (w,h,quality,n) — cache invalidation
        private int           _lastPeerGeneration = -1; // forces a send for new peers

        // ── State ─────────────────────────────────────────────────────────────

        // True from frame acceptance until the last worker deposits (or skips)
        // the frame. Covers the encode stage only — the send happens on the
        // sender thread behind the mailbox.
        private volatile bool _busy;

        // Safety valve: EncodeArrayToJPG off the main thread is standard practice
        // but not explicitly documented as thread-safe, and phase 2 runs four
        // encodes concurrently. If any worker encode ever throws, this flips
        // permanently (one warning is logged) and every subsequent frame is
        // encoded synchronously on the main thread as a single slice.
        private volatile bool _threadedEncodeBroken;

        private UdpVideoSender _sink;
        private volatile int   _quality = 75;

        private byte[] _fallbackBuffer; // main-thread valve path only

        // ── Stats ─────────────────────────────────────────────────────────────

        private volatile float _lastEncodeMs;
        private int _framesSkippedUnchanged;

        /// <summary>Milliseconds of the most recent encode (slowest slice — the
        /// wall-clock cost, since slices run in parallel).</summary>
        public float LastEncodeMs => _lastEncodeMs;

        /// <summary>Frames dropped because every slice matched the previous
        /// frame byte-for-byte (static scene — nothing worth sending).</summary>
        public int FramesSkippedUnchanged => _framesSkippedUnchanged;

        /// <summary>
        /// <c>true</c> while a frame is being encoded.
        /// The capture loop uses this to skip frames during back-pressure.
        /// </summary>
        public bool IsEncoding => _busy;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>Creates the encoder.</summary>
        /// <param name="sink">Video sender whose mailbox receives encoded frames.</param>
        /// <param name="quality">Initial JPEG quality [0, 100] (default 75).</param>
        public FrameEncoder(UdpVideoSender sink, int quality = 75)
        {
            _sink    = sink;
            _quality = Mathf.Clamp(quality, 1, 100);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Updates the JPEG quality used for subsequent encodes.</summary>
        /// <param name="quality">Quality value [1, 100].</param>
        public void SetQuality(int quality) => _quality = Mathf.Clamp(quality, 1, 100);

        /// <summary>Resets statistics counters (call when streaming (re-)starts).</summary>
        public void ResetStats()
        {
            _lastEncodeMs = 0f;
            _framesSkippedUnchanged = 0;
        }

        /// <summary>
        /// Submits a captured <see cref="Texture2D"/> for slice-parallel JPEG
        /// encoding and subsequent send.
        /// <para>
        /// <b>Must be called from the Unity main thread.</b>
        /// The texture's CPU-side pixels are copied into per-worker buffers
        /// before this method returns; the caller keeps ownership of the
        /// texture and may reuse it for the next capture immediately.
        /// </para>
        /// </summary>
        /// <param name="texture">
        /// Texture to encode. Must be <see cref="TextureFormat.RGB24"/> with valid
        /// CPU-side pixel data (i.e. <c>ReadPixels</c> or
        /// <c>LoadRawTextureData</c> has already run — no <c>Apply()</c> needed,
        /// the encoder never touches the GPU copy).
        /// </param>
        /// <returns>
        /// <c>true</c> if the frame was accepted; <c>false</c> if the previous
        /// frame is still encoding or no peer can receive video.
        /// </returns>
        public bool SubmitFrame(Texture2D texture)
        {
            if (texture == null) return false;
            if (texture.format != TextureFormat.RGB24)
            {
                GamePeekConstants.LogWarning(
                    $"[Encoder] Unsupported capture format {texture.format} (expected RGB24) — frame dropped.");
                return false;
            }
            return SubmitFrame(texture.GetRawTextureData<byte>(), texture.width, texture.height);
        }

        /// <summary>
        /// Submits one frame of raw RGB24 pixel data (display-ready gamma bytes,
        /// tightly packed rows, <b>bottom row first</b> — what every capture
        /// path delivers) for slice-parallel encoding and subsequent send.
        /// <para>
        /// <b>Must be called from the Unity main thread.</b> The data is copied
        /// into per-worker buffers before this method returns, so the caller's
        /// <see cref="NativeArray{T}"/> may be invalidated afterwards.
        /// </para>
        /// </summary>
        /// <param name="rawRgb24">Tightly packed RGB24 pixel rows, bottom row first.</param>
        /// <param name="width">Frame width in pixels.</param>
        /// <param name="height">Frame height in pixels.</param>
        /// <returns>
        /// <c>true</c> if the frame was accepted; <c>false</c> if the previous
        /// frame is still encoding or no peer can receive video.
        /// </returns>
        public bool SubmitFrame(NativeArray<byte> rawRgb24, int width, int height)
        {
            if (_busy) return false;
            // Skip work while nobody can receive video — peers still inside
            // their 2 s punch window don't count (they'd drop the frame anyway).
            if (_sink == null || !_sink.HasReadyPeers) return false;
            if (width <= 0 || height <= 0) return false;

            int required = width * height * 3;
            if (rawRgb24.Length < required) return false;

            int quality = _quality;

            // ── Safety-valve path: synchronous single-slice main-thread encode
            if (_threadedEncodeBroken)
            {
                if (_fallbackBuffer == null || _fallbackBuffer.Length != required)
                    _fallbackBuffer = new byte[required];
                NativeArray<byte>.Copy(rawRgb24, _fallbackBuffer, required);

                byte[] jpeg = EncodeOnMainThread(_fallbackBuffer, width, height, quality);
                if (jpeg == null || jpeg.Length == 0) return false;

                _sink.SubmitEncodedFrame(new EncodedVideoFrame
                {
                    Width  = width,
                    Height = height,
                    Slices = new[] { jpeg },
                });
                return true;
            }

            // ── Parallel path ─────────────────────────────────────────────────
            EnsureWorkers();
            int slices = Math.Min(_workers.Length, height); // ≥1 row per slice

            // Geometry/quality change invalidates the per-worker unchanged
            // caches — a cached JPEG of different dimensions must never ship.
            long key = ((long)width << 36) ^ ((long)height << 12)
                       ^ ((long)quality << 4) ^ (uint)slices;
            bool invalidate = key != _geometryKey;
            _geometryKey = key;

            // A peer that just became ready must receive the next frame even
            // when the scene is static — clear the unchanged caches so this
            // frame encodes and ships in full.
            int generation = _sink.PeerGeneration;
            if (generation != _lastPeerGeneration)
            {
                _lastPeerGeneration = generation;
                invalidate = true;
            }

            _pending = new PendingFrame
            {
                Width      = width,
                Height     = height,
                Quality    = quality,
                SlicesUsed = slices,
            };
            _busy = true;
            Interlocked.Exchange(ref _workersRemaining, slices);

            for (int i = 0; i < slices; i++)
            {
                var worker = _workers[i];

                // Display slice i = rows [i·H/N, (i+1)·H/N) (floor, §6). In the
                // bottom-first buffer that is the contiguous region starting at
                // (H − rowEnd)·W·3.
                int rowStart = i * height / slices;
                int rowEnd   = (i + 1) * height / slices;
                int rows     = rowEnd - rowStart;
                int offset   = (height - rowEnd) * width * 3;
                int length   = rows * width * 3;

                if (worker.Raw == null || worker.Raw.Length != length)
                    worker.Raw = new byte[length];
                NativeArray<byte>.Copy(rawRgb24, offset, worker.Raw, 0, length);
                worker.Rows = rows;

                if (invalidate)
                {
                    worker.LastHash = 0;
                    worker.LastJpeg = null;
                }

                worker.Wake.Set();
            }
            return true;
        }

        /// <summary>
        /// Stops the worker threads and clears state. Called by
        /// <see cref="FrameCapture.Stop"/> on streaming teardown and, as a safety
        /// net, before assembly reloads. Safe to call multiple times; workers
        /// are restarted automatically should the encoder be reused afterwards.
        /// </summary>
        public void Stop()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;

            var workers = _workers;
            _workers = null;
            _running = false;

            if (workers != null)
            {
                foreach (var worker in workers)
                    worker.Wake?.Set();

                // Shared join deadline across all workers — teardown (and the
                // domain reload path in particular) must be bounded regardless
                // of thread count.
                var deadline = Stopwatch.StartNew();
                bool allJoined = true;
                foreach (var worker in workers)
                {
                    int remaining = (int)Math.Max(0, 2000 - deadline.ElapsedMilliseconds);
                    if (!(worker.Thread?.Join(remaining) ?? true)) allJoined = false;
                }

                if (allJoined)
                {
                    foreach (var worker in workers)
                        worker.Wake?.Dispose();
                }
                else
                {
                    GamePeekConstants.LogWarning(
                        "[Encoder] Not all encode workers stopped within 2 s; they are background threads and cannot keep the editor alive.");
                }
            }

            _busy = false;
        }

        // ── Main-thread fallback encode ───────────────────────────────────────

        /// <summary>
        /// Synchronous main-thread encode, used only after the off-thread safety
        /// valve has tripped (see <see cref="_threadedEncodeBroken"/>).
        /// </summary>
        private byte[] EncodeOnMainThread(byte[] raw, int width, int height, int quality)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return ImageConversion.EncodeArrayToJPG(
                    raw, EncodeFormat, (uint)width, (uint)height, 0, quality);
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Encoder] JPEG encode failed: {ex.Message}");
                return null;
            }
            finally
            {
                sw.Stop();
                _lastEncodeMs = (float)sw.Elapsed.TotalMilliseconds;
            }
        }

        // ── Worker pool ───────────────────────────────────────────────────────

        /// <summary>
        /// Lazily starts the worker pool, restarting it if any thread died.
        /// Called from the main thread only. Pool size:
        /// min(<see cref="SliceCount"/>, cores − 2), at least 1 — an encode
        /// worker per core would starve the editor on small machines.
        /// </summary>
        private void EnsureWorkers()
        {
            if (_workers != null && _running)
            {
                bool healthy = true;
                foreach (var worker in _workers)
                    if (worker.Thread == null || !worker.Thread.IsAlive) { healthy = false; break; }
                if (healthy) return;
                Stop();
            }

            // Belt-and-braces: ConnectionManager stops streaming (and with it
            // this encoder, via FrameCapture.Stop) before assembly reloads, but
            // a leaked worker must never outlive the domain. Re-armed together
            // with the pool; Stop() removes it.
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;

            int count = Mathf.Clamp(Environment.ProcessorCount - 2, 1, SliceCount);
            _running  = true;
            _workers  = new Worker[count];
            for (int i = 0; i < count; i++)
            {
                var worker = new Worker { Wake = new AutoResetEvent(false) };
                int index  = i;
                worker.Thread = new Thread(() => WorkerLoop(index))
                {
                    Name         = $"GamePeek Encode {index}",
                    IsBackground = true,
                };
                _workers[i] = worker;
                worker.Thread.Start();
            }
        }

        /// <summary>
        /// One worker: wait for a job, hash the slice, encode it (or reuse the
        /// cached JPEG when unchanged), and — as the last worker to finish —
        /// assemble and deposit the frame.
        /// </summary>
        private void WorkerLoop(int index)
        {
            try
            {
                while (_running)
                {
                    var workers = _workers;
                    if (workers == null || index >= workers.Length) break;
                    var worker = workers[index];

                    worker.Wake.WaitOne();
                    if (!_running) break;

                    var pending = _pending; // struct copy; stable while _busy
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        ulong hash = Fnv1a64(worker.Raw);
                        if (hash == worker.LastHash && worker.LastJpeg != null)
                        {
                            // Identical pixels — the previous encode is still
                            // byte-valid. Skip the expensive part entirely.
                            worker.Jpeg      = worker.LastJpeg;
                            worker.Unchanged = true;
                        }
                        else
                        {
                            worker.Unchanged = false;
                            byte[] jpeg = ImageConversion.EncodeArrayToJPG(
                                worker.Raw, EncodeFormat,
                                (uint)pending.Width, (uint)worker.Rows, 0, pending.Quality);
                            worker.Jpeg     = jpeg;
                            worker.LastJpeg = jpeg;
                            worker.LastHash = hash;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Safety valve: never retry threaded encoding this
                        // session — SubmitFrame reverts to main-thread encode.
                        worker.Jpeg = null;
                        _threadedEncodeBroken = true;
                        GamePeekConstants.LogWarning(
                            "[Encoder] Off-thread JPEG encode failed — reverting to " +
                            $"single-slice main-thread encoding for this session: {ex.Message}");
                    }
                    finally
                    {
                        sw.Stop();
                        worker.EncodeMs = (float)sw.Elapsed.TotalMilliseconds;
                        if (Interlocked.Decrement(ref _workersRemaining) == 0)
                            CompleteFrame();
                    }
                }
            }
            catch
            {
                // WaitOne on a disposed handle after an unclean stop, or a thread
                // abort during domain unload — the loop simply ends.
                // EnsureWorkers starts a fresh pool if the encoder is used again.
            }
        }

        /// <summary>
        /// Runs on whichever worker finished last: aggregates the slices and
        /// either deposits the frame in the sender mailbox, skips it (every
        /// slice unchanged), or drops it (an encode failed — the valve has
        /// already tripped). Always clears <see cref="_busy"/>.
        /// </summary>
        private void CompleteFrame()
        {
            try
            {
                var workers = _workers;
                var pending = _pending;
                if (workers == null) return;

                int   slices       = pending.SlicesUsed;
                bool  allUnchanged = true;
                bool  anyFailed    = false;
                float maxMs        = 0f;

                for (int i = 0; i < slices; i++)
                {
                    var worker = workers[i];
                    if (worker.Jpeg == null || worker.Jpeg.Length == 0) anyFailed = true;
                    if (!worker.Unchanged) allUnchanged = false;
                    if (worker.EncodeMs > maxMs) maxMs = worker.EncodeMs;
                }
                _lastEncodeMs = maxMs;

                if (anyFailed) return;

                if (allUnchanged)
                {
                    // Static scene: the phone keeps showing the identical
                    // previous frame; sending it again buys nothing (§6 — a
                    // skipped frameId is indistinguishable from a lost one).
                    Interlocked.Increment(ref _framesSkippedUnchanged);
                    return;
                }

                var slicesOut = new byte[slices][];
                for (int i = 0; i < slices; i++)
                    slicesOut[i] = workers[i].Jpeg;

                _sink?.SubmitEncodedFrame(new EncodedVideoFrame
                {
                    Width  = pending.Width,
                    Height = pending.Height,
                    Slices = slicesOut,
                });
            }
            finally
            {
                _busy = false;
            }
        }

        // ── Hashing ───────────────────────────────────────────────────────────

        /// <summary>FNV-1a 64 over the raw slice bytes (≈0.5 ms per 700 KB slice).</summary>
        private static ulong Fnv1a64(byte[] data)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
