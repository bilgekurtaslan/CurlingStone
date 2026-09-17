using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;

namespace GamePeek
{
    /// <summary>
    /// Manages OS-level firewall rules required for the GamePeek servers.
    /// <para>
    /// On <b>Windows</b>: uses an elevated PowerShell script to:
    /// (1) remove any application-level block rules Windows auto-created when the
    ///     "Allow Unity through firewall?" prompt was dismissed, and
    /// (2) add port-based allow rules for GamePeek — <b>TCP and UDP</b> over the
    ///     same range (PROTOCOL.md §2). The UDP rule is what admits PUNCH/SENSOR
    ///     datagrams: PUNCH is the first packet on its flow, so stateful reply
    ///     filtering never lets it in without an explicit rule.
    /// The result is persisted in <see cref="EditorPrefs"/> so setup only runs once.
    /// </para>
    /// <para>
    /// On <b>macOS / Linux</b>: no-op — the OS automatically prompts the user when
    /// a process first binds to a port.
    /// </para>
    /// </summary>
    public static class FirewallHelper
    {
        private const string PrefKey  = "GamePeek_FirewallConfigured";
        // Port range the persisted rules cover. Legacy installs (flag set,
        // no range keys) created a single-port rule for DefaultPort.
        private const string PrefKeyRangeStart = "GamePeek_FirewallPortStart";
        private const string PrefKeyRangeEnd   = "GamePeek_FirewallPortEnd";
        // Rule-set schema version. Bumped to 2 when the UDP rule was added so
        // pre-v2 installs (TCP-only rule, no version key) re-run elevation once.
        private const string PrefKeyRuleVersion = "GamePeek_FirewallRuleVersion";
        private const int    CurrentRuleVersion = 2;
        private const string RuleName = "GamePeek";

        /// <summary>
        /// Ensures inbound TCP <em>and</em> UDP firewall rules exist covering the
        /// whole port-retry range <c>[port, port + PortBindAttempts - 1]</c>, so
        /// fallback ports chosen when <paramref name="port"/> is busy are also
        /// reachable (the UDP video socket binds inside the same range).
        /// On Windows, runs an elevated PowerShell script once per range/version
        /// and persists both in <see cref="EditorPrefs"/>.
        /// </summary>
        /// <param name="port">Base port (defaults to <see cref="GamePeekConstants.DefaultPort"/>).</param>
        public static void EnsureFirewallRule(int port = GamePeekConstants.DefaultPort)
        {
#if UNITY_EDITOR_WIN
            int rangeStart = port;
            int rangeEnd   = Math.Min(port + GamePeekConstants.PortBindAttempts - 1, 65535);

            if (EditorPrefs.GetBool(PrefKey, false))
            {
                // Legacy installs stored no range — their rule covers DefaultPort
                // only — and no version — their rule set is TCP-only.
                int coveredStart   = EditorPrefs.GetInt(PrefKeyRangeStart, GamePeekConstants.DefaultPort);
                int coveredEnd     = EditorPrefs.GetInt(PrefKeyRangeEnd,   GamePeekConstants.DefaultPort);
                int coveredVersion = EditorPrefs.GetInt(PrefKeyRuleVersion, 1);
                if (coveredVersion >= CurrentRuleVersion &&
                    rangeStart >= coveredStart && rangeEnd <= coveredEnd)
                    return;
            }

            AddWindowsFirewallRule(rangeStart, rangeEnd);
#endif
        }

        /// <summary>
        /// Clears the stored flag and immediately re-runs firewall setup.
        /// Use this from the GamePeek window or the menu item below to test
        /// the setup flow on your own machine.
        /// </summary>
        public static void ResetAndReConfigure()
        {
            ResetFlag();
            EnsureFirewallRule(GamePeekConstants.DefaultPort);
        }

        /// <summary>
        /// Clears the stored flag so the rule will be re-evaluated on the next
        /// <see cref="EnsureFirewallRule"/> call. Useful after manually deleting the rule.
        /// </summary>
        public static void ResetFlag()
        {
            EditorPrefs.DeleteKey(PrefKey);
            EditorPrefs.DeleteKey(PrefKeyRangeStart);
            EditorPrefs.DeleteKey(PrefKeyRangeEnd);
            EditorPrefs.DeleteKey(PrefKeyRuleVersion);
        }

        /// <summary>
        /// Returns <c>true</c> when the firewall rule has already been successfully added.
        /// </summary>
        public static bool IsConfigured => EditorPrefs.GetBool(PrefKey, false);

#if UNITY_EDITOR_WIN
        private static void AddWindowsFirewallRule(int rangeStart, int rangeEnd)
        {
            string localPort = rangeStart == rangeEnd
                ? rangeStart.ToString()
                : $"{rangeStart}-{rangeEnd}";
            try
            {
                // Get the currently-running Unity Editor executable path so we can
                // clear any app-level block rule Windows created when the "Allow Unity
                // through firewall?" prompt was previously dismissed or cancelled.
                string unityExe = Process.GetCurrentProcess().MainModule?.FileName
                                  ?? string.Empty;

                // Escape single quotes for PowerShell string literals.
                string safeExe  = unityExe.Replace("'", "''");

                // PowerShell script (written to a temp file to avoid cmd-line escaping issues):
                //   1. Remove any inbound block rules targeting this Unity executable.
                //   2. Remove stale GamePeek port rules (idempotent re-run safety;
                //      also removes the pre-v2 TCP-only rule).
                //   3. Add fresh port-based allow rules covering all profiles:
                //      TCP for the control channel, UDP for video PUNCH/SENSOR
                //      datagrams (PROTOCOL.md §2).
                string script =
                    "# Step 1 – remove block rules Windows auto-created for Unity Editor\n" +
                    $"$exe = '{safeExe}'\n" +
                    "Get-NetFirewallRule -Direction Inbound -Action Block -ErrorAction SilentlyContinue | ForEach-Object {\n" +
                    "    $filter = $_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue\n" +
                    "    if ($filter -and $filter.Program -eq $exe) { Remove-NetFirewallRule -Name $_.Name }\n" +
                    "}\n" +
                    $"# Step 2 – remove stale GamePeek rules\n" +
                    $"Remove-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue\n" +
                    $"# Step 3 – add allow rules (TCP control + UDP video)\n" +
                    $"New-NetFirewallRule -DisplayName '{RuleName}' -Direction Inbound " +
                    $"-Action Allow -Protocol TCP -LocalPort {localPort} -Profile Any | Out-Null\n" +
                    $"New-NetFirewallRule -DisplayName '{RuleName}' -Direction Inbound " +
                    $"-Action Allow -Protocol UDP -LocalPort {localPort} -Profile Any | Out-Null\n";

                string tmpScript = Path.Combine(Path.GetTempPath(), "gamepeek_fw.ps1");
                File.WriteAllText(tmpScript, script);

                var psi = new ProcessStartInfo(
                    "powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmpScript}\"")
                {
                    UseShellExecute = true,
                    Verb            = "runas",   // one-time UAC elevation
                    WindowStyle     = ProcessWindowStyle.Hidden,
                    CreateNoWindow  = true,
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);

                try { File.Delete(tmpScript); } catch { /* best-effort cleanup */ }

                if (proc?.ExitCode != 0)
                {
                    GamePeekConstants.LogWarning(
                        $"[Firewall] Setup may not have completed (PowerShell exit {proc?.ExitCode}). " +
                        "UAC may have been denied. Will retry on next Start.");
                    return;
                }

                EditorPrefs.SetBool(PrefKey, true);
                EditorPrefs.SetInt(PrefKeyRangeStart,  rangeStart);
                EditorPrefs.SetInt(PrefKeyRangeEnd,    rangeEnd);
                EditorPrefs.SetInt(PrefKeyRuleVersion, CurrentRuleVersion);
                GamePeekConstants.Log($"[Firewall] Rules '{RuleName}' configured for TCP+UDP {localPort} on all profiles.");
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning(
                    $"[Firewall] Could not configure automatically: {ex.Message}\n" +
                    "Run this in an elevated PowerShell:\n" +
                    $"  New-NetFirewallRule -DisplayName \"{RuleName}\" -Direction Inbound " +
                    $"-Action Allow -Protocol TCP -LocalPort {localPort} -Profile Any\n" +
                    $"  New-NetFirewallRule -DisplayName \"{RuleName}\" -Direction Inbound " +
                    $"-Action Allow -Protocol UDP -LocalPort {localPort} -Profile Any");
            }
        }
#endif
    }
}
