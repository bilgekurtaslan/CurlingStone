using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEditor;

namespace GamePeek
{
    /// <summary>A candidate network interface that can be used for binding.</summary>
    public readonly struct NetworkInterfaceOption
    {
        /// <summary>Human-readable label shown in the dropdown, e.g. <c>"Ethernet (10.0.1.5)"</c>.</summary>
        public readonly string Label;
        /// <summary>IPv4 address string.</summary>
        public readonly string IP;

        public NetworkInterfaceOption(string label, string ip)
        {
            Label = label;
            IP    = ip;
        }
    }

    /// <summary>
    /// Enumerates physical/wireless network interfaces and picks the best local IP
    /// for GamePeek to advertise and bind to, filtering out virtual adapters (VMware,
    /// Hyper-V, VirtualBox, VPN tunnels, loopback, etc.).
    /// <para>
    /// The user's manual override is persisted via <see cref="EditorPrefs"/> and
    /// takes priority over the automatic selection.
    /// </para>
    /// </summary>
    public static class NetworkInterfaceSelector
    {
        // ── EditorPrefs key ───────────────────────────────────────────────────
        private const string PrefSelectedIP = "GamePeek_SelectedIP";

        // ── Virtual-adapter name/description fragments (lower-case) ──────────
        private static readonly string[] VirtualKeywords =
        {
            "vmware", "virtualbox", "hyper-v", "vethernet",
            "loopback", "tunnel", "tap-", "tapwindows", "pseudo",
            "teredo", "isatap", "6to4", "npcap", "miniport",
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all Up, non-loopback, unicast IPv4 interfaces as dropdown options —
        /// including virtual adapters — so the user can manually select any of them.
        /// Virtual-adapter filtering is applied only during auto-selection (<see cref="GetBestIP"/>).
        /// Never throws.
        /// </summary>
        public static List<NetworkInterfaceOption> GetCandidates()
        {
            var result = new List<NetworkInterfaceOption>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)             continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)  continue;

                    foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(uni.Address))                       continue;

                        string ip    = uni.Address.ToString();
                        string label = $"{nic.Name} ({ip})";
                        result.Add(new NetworkInterfaceOption(label, ip));
                    }
                }
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Network] Could not enumerate interfaces: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Returns the effective IP to use: the user's saved override if set,
        /// otherwise the result of <see cref="GetBestIP"/>.
        /// </summary>
        public static string GetEffectiveIP()
        {
            string saved = EditorPrefs.GetString(PrefSelectedIP, string.Empty);
            return string.IsNullOrEmpty(saved) ? GetBestIP() : saved;
        }

        /// <summary>Returns the raw saved override IP (empty string = auto).</summary>
        public static string GetSavedIP()
            => EditorPrefs.GetString(PrefSelectedIP, string.Empty);

        /// <summary>Persists a manual IP override. Pass <c>null</c> or empty to revert to auto.</summary>
        public static void SaveIP(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                EditorPrefs.DeleteKey(PrefSelectedIP);
            else
                EditorPrefs.SetString(PrefSelectedIP, ip);
        }

        // ── Auto-selection ────────────────────────────────────────────────────

        /// <summary>
        /// Picks the best local IP using the following priority order:
        /// <list type="number">
        ///   <item>Interface that has a default IPv4 gateway (the actual routing interface).</item>
        ///   <item>Ethernet or Wi-Fi interface (over other types).</item>
        ///   <item>First remaining candidate.</item>
        /// </list>
        /// Returns <c>"127.0.0.1"</c> if no suitable interface is found.
        /// </summary>
        public static string GetBestIP()
        {
            // Collect candidates together with their NIC so we can inspect gateways.
            var nics    = new List<(NetworkInterface nic, string ip)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)            continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (IsVirtual(nic))                                            continue;

                    foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(uni.Address))                       continue;

                        nics.Add((nic, uni.Address.ToString()));
                        break; // one IPv4 per NIC is enough for selection
                    }
                }
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Network] Auto-select error: {ex.Message}");
            }

            if (nics.Count == 0) return "127.0.0.1";
            if (nics.Count == 1) return nics[0].ip;

            // Priority 1 — interface that carries the default route (has a gateway)
            foreach (var (nic, ip) in nics)
            {
                try
                {
                    var gateways = nic.GetIPProperties().GatewayAddresses;
                    foreach (var gw in gateways)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ip;
                    }
                }
                catch { /* skip */ }
            }

            // Priority 2 — prefer Ethernet / Wi-Fi
            foreach (var (nic, ip) in nics)
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    return ip;
            }

            return nics[0].ip;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsVirtual(NetworkInterface nic)
        {
            string name = nic.Name.ToLowerInvariant();
            string desc = nic.Description.ToLowerInvariant();
            foreach (string kw in VirtualKeywords)
            {
                if (name.Contains(kw) || desc.Contains(kw))
                    return true;
            }
            return false;
        }
    }
}
