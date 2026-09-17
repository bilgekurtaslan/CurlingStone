using UnityEngine;

namespace GamePeek
{
    /// <summary>Controls how verbose GamePeek's console output is.</summary>
    public enum LogLevel
    {
        /// <summary>No console output at all.</summary>
        None,
        /// <summary>Errors only.</summary>
        Error,
        /// <summary>Warnings and errors.</summary>
        Warning,
        /// <summary>All messages (info, warnings, errors).</summary>
        All,
    }

    /// <summary>
    /// Shared constants, port numbers, and logging utilities for the GamePeek plugin.
    /// All other GamePeek components reference this class for configuration defaults.
    /// </summary>
    public static class GamePeekConstants
    {
        // ── Version ──────────────────────────────────────────────────────────
        /// <summary>Current plugin version string.</summary>
        public const string Version = "2.5";

        // ── Networking ───────────────────────────────────────────────────────
        /// <summary>Wire protocol version (docs~/PROTOCOL.md). HELLO/WELCOME carry it; mDNS TXT and the QR advertise it as <c>v=2</c>.</summary>
        public const int ProtoVersion = 2;

        /// <summary>Default TCP control port (Unity listens, phone connects).</summary>
        public const int DefaultPort = 7777;

        /// <summary>How many consecutive ports StartStreaming tries (requested port … port+9) before giving up. The UDP socket retries the same range.</summary>
        public const int PortBindAttempts = 10;

        /// <summary>Seconds after WELCOME before a session with no PUNCH falls back to video-over-TCP (PROTOCOL.md §3).</summary>
        public const double PunchTimeoutSeconds = 2.0;

        /// <summary>Seconds of TCP silence before a session is dropped as dead (§3 — the phone PINGs every 2 s).</summary>
        public const double IdleDisconnectSeconds = 10.0;

        // ── mDNS / DNS-SD ────────────────────────────────────────────────────
        /// <summary>mDNS service type broadcast on the local network.</summary>
        public const string ServiceType = "_unipeek._tcp";

        /// <summary>mDNS multicast group (RFC 6762).</summary>
        public const string MdnsMulticastAddress = "224.0.0.251";

        /// <summary>mDNS UDP port (RFC 6762).</summary>
        public const int MdnsPort = 5353;

        // ── RTT thresholds (milliseconds) ───────────────────────────────────
        // The phone measures RTT via TCP PING and reports it in STATS (1 Hz);
        // these thresholds only color the display.
        /// <summary>RTT below this value is shown as green.</summary>
        public const float RttGreenMs  =  20f;
        /// <summary>RTT below this value is shown as yellow.</summary>
        public const float RttYellowMs =  50f;
        /// <summary>RTT below this value is shown as orange.</summary>
        public const float RttOrangeMs = 100f;
        // RTT ≥ RttOrangeMs is shown as red.

        // ── EditorPrefs keys ─────────────────────────────────────────────────
        /// <summary>EditorPrefs key for the auto-stop-on-play-mode toggle.</summary>
        public const string PrefAutoStopPlay = "GamePeek_AutoStopPlay";

        /// <summary>EditorPrefs key for the user-defined editor display name (shown in Flutter discovery).</summary>
        public const string PrefEditorName = "GamePeek_EditorName";

        /// <summary>
        /// EditorPrefs key that persists across domain reloads to indicate streaming
        /// should auto-restart (set when streaming is started with "Only run in Play Mode" OFF).
        /// Cleared when the user manually stops streaming.
        /// </summary>
        public const string PrefPersistStreaming = "GamePeek_PersistStreaming";

        /// <summary>EditorPrefs key for the user-configured TCP control port.</summary>
        public const string PrefPort = "GamePeek_Port";

        /// <summary>EditorPrefs key for the active <see cref="LogLevel"/>.</summary>
        public const string PrefLogLevel = "GamePeek_LogLevel";

        /// <summary>EditorPrefs key for auto-starting streaming when entering Play Mode.</summary>
        public const string PrefAutoStartOnPlay = "GamePeek_AutoStartOnPlay";

        /// <summary>EditorPrefs key for the show-touch-gizmos toggle.</summary>
        public const string PrefShowTouchGizmos = "GamePeek_ShowTouchGizmos";

        // ── Runtime log level ─────────────────────────────────────────────────
        /// <summary>Active log verbosity. Set by the editor window; read by the log helpers below.</summary>
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.All;

        // ── Logging helpers ──────────────────────────────────────────────────
        /// <summary>Writes an info-level message tagged with [GamePeek] when <see cref="CurrentLogLevel"/> is <c>All</c>.</summary>
        public static void Log(string message)
        {
            if (CurrentLogLevel == LogLevel.All)
                Debug.Log($"[GamePeek] {message}");
        }

        /// <summary>Writes a warning tagged with [GamePeek] when <see cref="CurrentLogLevel"/> is <c>Warning</c> or higher.</summary>
        public static void LogWarning(string message)
        {
            if (CurrentLogLevel >= LogLevel.Warning)
                Debug.LogWarning($"[GamePeek] {message}");
        }

        /// <summary>Writes an error tagged with [GamePeek] unless <see cref="CurrentLogLevel"/> is <c>None</c>.</summary>
        public static void LogError(string message)
        {
            if (CurrentLogLevel >= LogLevel.Error)
                Debug.LogError($"[GamePeek] {message}");
        }
    }
}
