using System;
using System.Security.Cryptography;

namespace GamePeek
{
    // ── Wire protocol v2 ──────────────────────────────────────────────────────
    // This file is the C# side of docs~/PROTOCOL.md (rev B, agreed baseline).
    // Nothing here may deviate from that document; canonical byte fixtures for
    // both ends live in docs~/fixtures/.

    /// <summary>TCP control-channel message types (PROTOCOL.md §4).</summary>
    public static class MsgType
    {
        public const byte Hello    = 0x01; // P→E  JSON
        public const byte Welcome  = 0x02; // E→P  JSON
        public const byte Config   = 0x03; // P→E  JSON
        public const byte Touch    = 0x04; // P→E  JSON
        public const byte PlayMode = 0x05; // E→P  JSON
        public const byte Shutdown = 0x06; // E→P  empty
        public const byte Ping     = 0x07; // P→E  8-byte u64 LE timestamp (opaque)
        public const byte Pong     = 0x08; // E→P  echo of Ping payload
        public const byte VideoTcp = 0x09; // E→P  one video chunk (§6 body from `codec` onward)
        public const byte Stats    = 0x0A; // P→E  JSON, 1 Hz
        public const byte Error    = 0x0B; // E→P  JSON (§4.6)
        public const byte Sensor   = 0x0C; // P→E  binary §5.1 body (TCP-fallback sensor path)
    }

    /// <summary>UDP datagram packet types (PROTOCOL.md §5).</summary>
    public static class PktType
    {
        public const byte Video  = 0x01; // E→P
        public const byte Punch  = 0x02; // P→E  empty body
        public const byte Sensor = 0x03; // P→E  §5.1 body
        public const byte Audio  = 0x04; // E→P  reserved for future audio streaming — never sent in v2.0
    }

    /// <summary>Video codec registry (PROTOCOL.md §6.3).</summary>
    public static class VideoCodec
    {
        /// <summary>Baseline JPEG — always supported both ends (phases 1–3).</summary>
        public const byte Jpeg = 1;
        /// <summary>Hardware H.264 — phase 4; never sent before then.</summary>
        public const byte H264 = 2;
    }

    /// <summary>ERROR message codes (PROTOCOL.md §4.6). ERROR + close is terminal for the app.</summary>
    public static class ErrorCodes
    {
        /// <summary>HELLO.proto unsupported — app shows "update required".</summary>
        public const string VersionMismatch = "version_mismatch";
        /// <summary>Multi-device limit rejection (v1 message name kept).</summary>
        public const string ProRequired = "pro_required";
    }

    /// <summary>
    /// Byte-level framing helpers for protocol v2. All multi-byte integers are
    /// little-endian in both directions (PROTOCOL.md §1). Every method here is
    /// pure and thread-safe — they are called from the TCP receive threads, the
    /// UDP receive thread, and the video sender thread alike.
    /// </summary>
    public static class Wire
    {
        // ── Sizes and bounds ──────────────────────────────────────────────────

        /// <summary>TCP frame header: msgType (u8) + payloadLen (u32 LE).</summary>
        public const int TcpHeaderSize = 5;

        /// <summary>Sanity bound on a TCP payload; larger = protocol error, close socket.</summary>
        public const int MaxTcpPayload = 16 * 1024 * 1024;

        /// <summary>UDP datagram prefix: token (u32 LE) + pktType (u8).</summary>
        public const int UdpPrefixSize = 5;

        /// <summary>VIDEO body header (§6): codec..height, excluding the UDP prefix.</summary>
        public const int VideoBodyHeaderSize = 19;

        /// <summary>Full UDP VIDEO header: prefix + body header = 24 bytes.</summary>
        public const int VideoUdpHeaderSize = UdpPrefixSize + VideoBodyHeaderSize;

        /// <summary>Maximum chunk payload per UDP datagram (Tailscale/WireGuard MTU headroom).</summary>
        public const int MaxChunkPayload = 1100;

        /// <summary>SENSOR body length (§5.1): sensorType (u8) + x/y/z (f32 LE each).</summary>
        public const int SensorBodySize = 13;

        // ── Little-endian primitives ──────────────────────────────────────────

        /// <summary>Writes a u16 little-endian at <paramref name="offset"/>.</summary>
        public static void WriteU16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset]     = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        /// <summary>Writes a u32 little-endian at <paramref name="offset"/>.</summary>
        public static void WriteU32(byte[] buffer, int offset, uint value)
        {
            buffer[offset]     = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        /// <summary>Reads a u16 little-endian from <paramref name="offset"/>.</summary>
        public static ushort ReadU16(byte[] buffer, int offset)
            => (ushort)(buffer[offset] | (buffer[offset + 1] << 8));

        /// <summary>Reads a u32 little-endian from <paramref name="offset"/>.</summary>
        public static uint ReadU32(byte[] buffer, int offset)
            => (uint)(buffer[offset]
                      | (buffer[offset + 1] << 8)
                      | (buffer[offset + 2] << 16)
                      | (buffer[offset + 3] << 24));

        /// <summary>Reads an f32 little-endian from <paramref name="offset"/>.</summary>
        public static float ReadF32(byte[] buffer, int offset)
        {
            // BitConverter honours the machine byte order; every platform the
            // Unity editor runs on (x64/ARM64 mac, Windows, Linux) is
            // little-endian, which is the wire order — no swap needed.
            return BitConverter.ToSingle(buffer, offset);
        }

        // ── TCP framing ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds a complete TCP frame (header + payload copy) for
        /// <paramref name="payload"/>. Pass <c>null</c> or empty for
        /// empty-payload messages (SHUTDOWN).
        /// </summary>
        public static byte[] BuildTcpFrame(byte msgType, byte[] payload)
        {
            int len   = payload?.Length ?? 0;
            var frame = new byte[TcpHeaderSize + len];
            frame[0]  = msgType;
            WriteU32(frame, 1, (uint)len);
            if (len > 0) Buffer.BlockCopy(payload, 0, frame, TcpHeaderSize, len);
            return frame;
        }

        // ── VIDEO chunk header (§6) ───────────────────────────────────────────

        /// <summary>
        /// Writes the 19-byte VIDEO body header (codec through height) into
        /// <paramref name="buffer"/> at <paramref name="offset"/>. For UDP the
        /// caller first writes the 5-byte token/pktType prefix; for VIDEO_TCP
        /// this header starts the message payload directly (token omitted —
        /// the socket is the session).
        /// </summary>
        public static void WriteVideoBodyHeader(
            byte[] buffer, int offset,
            byte codec, uint frameId,
            byte sliceIndex, byte sliceCount,
            ushort chunkIndex, ushort chunkCount,
            uint sliceLen, ushort width, ushort height)
        {
            buffer[offset]     = codec;
            WriteU32(buffer, offset + 1, frameId);
            buffer[offset + 5] = sliceIndex;
            buffer[offset + 6] = sliceCount;
            WriteU16(buffer, offset + 7,  chunkIndex);
            WriteU16(buffer, offset + 9,  chunkCount);
            WriteU32(buffer, offset + 11, sliceLen);
            WriteU16(buffer, offset + 15, width);
            WriteU16(buffer, offset + 17, height);
        }

        /// <summary>Writes the 5-byte UDP datagram prefix (token + pktType).</summary>
        public static void WriteUdpPrefix(byte[] buffer, uint token, byte pktType)
        {
            WriteU32(buffer, 0, token);
            buffer[4] = pktType;
        }

        // ── Session tokens ────────────────────────────────────────────────────

        /// <summary>
        /// Generates a session token: a cryptographically random u32, unique
        /// among live sessions (PROTOCOL.md §4.2). Never time-seeded — two
        /// devices connecting in the same millisecond must not collide.
        /// </summary>
        /// <param name="isTaken">
        /// Returns <c>true</c> when a candidate collides with a live session
        /// (also excludes 0, which this method never returns).
        /// </param>
        public static uint GenerateToken(Func<uint, bool> isTaken)
        {
            var bytes = new byte[4];
            using var rng = RandomNumberGenerator.Create();
            for (int attempt = 0; attempt < 64; attempt++)
            {
                rng.GetBytes(bytes);
                uint candidate = ReadU32(bytes, 0);
                if (candidate != 0 && (isTaken == null || !isTaken(candidate)))
                    return candidate;
            }
            // 64 collisions among a handful of sessions is practically
            // impossible — treat it as an RNG failure rather than loop forever.
            throw new InvalidOperationException("Could not generate a unique session token.");
        }
    }

    // ── Phone→editor message POCOs (deserialized with JsonUtility) ───────────
    // v2 JSON payloads carry no "type" field — the msgType byte replaces it.
    // JsonUtility is main-thread-only: raw payloads are marshalled to the main
    // thread by ConnectionManager before these types are touched.

    /// <summary>HELLO payload (PROTOCOL.md §4.1) — the phone's handshake.</summary>
    [Serializable]
    public class HelloMessage
    {
        public int    proto;        // must equal GamePeekConstants.ProtoVersion
        public string client;       // "flutter"
        public string tier;         // "free" | "pro"
        public string deviceName;   // required in v2 — stale-session eviction keys on it
        public int    width;        // native screen width in pixels (0 if not provided)
        public int    height;       // native screen height in pixels (0 if not provided)
        public string orientation;  // "portrait" | "landscape" (empty if not provided)
        public int[]  codecs;       // decoder codec IDs the phone supports (§6.3)
    }

    /// <summary>CONFIG payload (PROTOCOL.md §4.3) — full set, host session only.</summary>
    [Serializable]
    public class ConfigMessage
    {
        public string resolution;   // e.g. "520x1131" — always portrait-ordered (short × long)
        public int    quality;      // 1–100 (0 = keep current)
        public int    fps;          // frames per second cap (0 = keep current)
        public bool   landscape;    // true when the device is in landscape orientation
        public int    codec;        // requested codec id (§6.3); JPEG until phase 4

        // Input-enable gates. Field initializers are the JsonUtility "absent
        // field" defaults — everything enabled, matching v1 behavior.
        public bool touchEnabled = true;
        public bool gyroEnabled  = true;
        public bool accelEnabled = true;
    }

    /// <summary>TOUCH payload (PROTOCOL.md §4.4), host session only.</summary>
    [Serializable]
    public class TouchMessage
    {
        public string phase;      // began | moved | ended | canceled (v1 "cancelled" also accepted)
        public float  x;          // normalised [0, 1]
        public float  y;          // normalised [0, 1]; y=0 is the top edge
        public int    fingerId;
    }

    // ── Editor→phone message POCOs (serialized with JsonUtility) ─────────────
    // Field order matches the PROTOCOL.md samples and docs~/fixtures/ exactly.

    /// <summary>PLAYMODE payload — sent right after WELCOME, then on every change.</summary>
    [Serializable]
    public class PlayModeMessage
    {
        public bool playing; // true = Play Mode, false = Edit Mode
    }

    /// <summary>WELCOME payload (PROTOCOL.md §4.2) — the editor's handshake reply.</summary>
    [Serializable]
    public class WelcomeMessage
    {
        public int    proto;       // always 2
        public string editorName;  // user-configured display name (machine name fallback)
        public string projectName; // Application.productName; app keys per-project config on it
        public int    udpPort;     // authoritative UDP video/sensor port
        public long   token;       // u32 session token — long, NOT int: routinely exceeds int32 (§4.2 JSON note)
        public int[]  codecs;      // encoder codec IDs offered (§6.3); phase 1–3: [1] (JPEG)
        public int    sliceCount;  // advisory slice layout; VIDEO packets are authoritative
    }

    /// <summary>ERROR payload (PROTOCOL.md §4.6). Followed by a flushed close; terminal for the app.</summary>
    [Serializable]
    public class ErrorMessage
    {
        public string code;     // see ErrorCodes
        public string message;  // human-readable detail
    }

    /// <summary>STATS payload (PROTOCOL.md §4.5), sent by the phone at 1 Hz from phase 2.</summary>
    [Serializable]
    public class StatsMessage
    {
        public float deliveredFps;    // complete frames rendered per second
        public int   completeFrames;  // over the last second
        public int   abandonedFrames; // reassembly slots abandoned (>250 ms) over the last second
        public float rttMs;           // phone-measured TCP PING round-trip
        public float decodeMs;        // mean JPEG decode time
    }
}
