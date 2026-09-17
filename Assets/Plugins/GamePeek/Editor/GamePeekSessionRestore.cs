using UnityEditor;
using UnityEngine;

namespace GamePeek
{
    /// <summary>
    /// Restarts streaming after a domain reload (script recompile or play-mode
    /// entry). <see cref="ConnectionManager"/> snapshots the live streaming state
    /// into <see cref="SessionState"/> in its <c>beforeAssemblyReload</c> handler;
    /// this class claims that snapshot once the new domain is up and brings the
    /// control server back on the same port so phones can auto-reconnect (they
    /// re-handshake and receive a fresh token/udpPort — PROTOCOL.md §3).
    /// <para>
    /// Runs independently of the GamePeek window — streaming survives reloads
    /// even when the window is closed. The window mirrors <see
    /// cref="ConnectionManager.StateChanged"/>, so its UI reflects the restart.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class GamePeekSessionRestore
    {
        static GamePeekSessionRestore()
        {
            // InitializeOnLoad runs before the editor is fully initialized —
            // defer one tick so windows, prefs, and the update loop are ready.
            EditorApplication.delayCall += TryResume;
        }

        private static void TryResume()
        {
            if (!SessionState.GetBool(ConnectionManager.SessionKeyResumeStreaming, false))
                return;

            // Claim the flag immediately (same pattern as the window's pending-start
            // key) so a skipped or failed resume can never fire again on a later reload.
            SessionState.EraseBool(ConnectionManager.SessionKeyResumeStreaming);

            // Respect "Run in Play Mode": if the reload landed us back in Edit Mode,
            // the window's own play-mode handlers decide when streaming restarts.
            bool requirePlayMode = EditorPrefs.GetBool(GamePeekConstants.PrefAutoStopPlay, true);
            if (requirePlayMode && !EditorApplication.isPlaying) return;

            var mgr = ConnectionManager.Instance;

            // The window's OnEnable restore may have already won the race —
            // never double-start.
            if (mgr.State != ConnectionState.Disconnected) return;

            // The window normally applies these in OnEnable — read them here too so
            // the resume behaves the same when the window is closed.
            GamePeekConstants.CurrentLogLevel = (LogLevel)EditorPrefs.GetInt(
                GamePeekConstants.PrefLogLevel, (int)GamePeekConstants.CurrentLogLevel);

            // Restore the negotiated stream parameters (set before StartStreaming so
            // the recreated capture + encoder match what the phone last requested).
            mgr.Config.Width   = SessionState.GetInt(ConnectionManager.SessionKeyResumeWidth,   mgr.Config.Width);
            mgr.Config.Height  = SessionState.GetInt(ConnectionManager.SessionKeyResumeHeight,  mgr.Config.Height);
            mgr.Config.Quality = SessionState.GetInt(ConnectionManager.SessionKeyResumeQuality, mgr.Config.Quality);
            mgr.Config.FpsCap  = SessionState.GetInt(ConnectionManager.SessionKeyResumeFps,     mgr.Config.FpsCap);
            mgr.SetCaptureMethod((CaptureMethod)SessionState.GetInt(
                ConnectionManager.SessionKeyResumeCaptureMethod, (int)mgr.ActiveCaptureMethod));

            int port = SessionState.GetInt(ConnectionManager.SessionKeyResumePort, mgr.Port);

            Application.runInBackground = true;

            if (mgr.StartStreaming(port))
                GamePeekConstants.Log($"[Reload] Streaming resumed on port {port} after domain reload.");
            else
                GamePeekConstants.LogWarning($"[Reload] Could not resume streaming on port {port} after domain reload.");
        }
    }
}
