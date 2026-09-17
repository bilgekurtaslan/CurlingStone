using System;
using UnityEditor;
using UnityEngine;
using QRCoder;

namespace GamePeek
{
    /// <summary>
    /// Generates QR code <see cref="Texture2D"/> images for the GamePeek Editor window.
    /// <para>
    /// The payload encoded in the QR is a custom-scheme URI that the companion Flutter app
    /// parses to discover the WebSocket host and port:
    /// <code>gamepeek://connect?ip=192.168.1.x&amp;port=7777&amp;name=DESKTOP-ABCD</code>
    /// </para>
    /// <para>Requires <c>QRCoder.dll</c> to be present under <c>Assets/Plugins/GamePeek/lib/</c>.</para>
    /// </summary>
    public static class QRCodeGenerator
    {
        // ── Internal state ────────────────────────────────────────────────────
        private static string _lastIp;
        private static Texture2D _cachedTexture;

        // Code-created textures are never cleaned up automatically by Unity — release
        // the cached QR before each domain reload so it doesn't leak across reloads.
        [InitializeOnLoadMethod]
        private static void RegisterReloadCleanup()
            => AssemblyReloadEvents.beforeAssemblyReload += DestroyCachedTexture;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a <see cref="Texture2D"/> QR code encoding the GamePeek connection
        /// payload for the given <paramref name="port"/>.
        /// <para>
        /// The result is cached and regenerated only when the machine's local IP
        /// address changes (e.g. after a network switch).
        /// </para>
        /// </summary>
        /// <param name="port">WebSocket port to embed in the payload (default 7777).</param>
        /// <param name="pixelsPerModule">Size of each QR dot in pixels (default 10).</param>
        /// <returns>A <see cref="Texture2D"/> containing the QR code, or <c>null</c> on error.</returns>
        public static Texture2D GetConnectionQR(int port = GamePeekConstants.DefaultPort, int pixelsPerModule = 10)
        {
            string currentIp = GetLocalIPv4();

            if (_cachedTexture != null && currentIp == _lastIp)
                return _cachedTexture;

            // IP changed (or first call) — regenerate
            _lastIp = currentIp;
            DestroyCachedTexture();

            string payload = BuildPayload(currentIp, port);
            _cachedTexture = GenerateQRTexture(payload, pixelsPerModule);
            return _cachedTexture;
        }

        /// <summary>
        /// Returns the local IPv4 address that will be embedded in the QR payload.
        /// Respects the user's manual interface override (saved via <see cref="NetworkInterfaceSelector"/>);
        /// falls back to smart auto-selection that prefers physical Ethernet/Wi-Fi interfaces and
        /// skips virtual adapters (VMware, Hyper-V, VirtualBox, VPN tunnels, etc.).
        /// Returns <c>"127.0.0.1"</c> if no suitable address is found.
        /// </summary>
        public static string GetLocalIPv4()
            => NetworkInterfaceSelector.GetEffectiveIP();

        /// <summary>
        /// Destroys the cached QR <see cref="Texture2D"/> and resets the IP cache,
        /// forcing a fresh generation on the next call to <see cref="GetConnectionQR"/>.
        /// </summary>
        public static void Invalidate()
        {
            _lastIp = null;
            DestroyCachedTexture();
        }

        /// <summary>
        /// Generates a QR code <see cref="Texture2D"/> from an arbitrary string payload.
        /// </summary>
        /// <param name="payload">The text to encode.</param>
        /// <param name="pixelsPerModule">Size of each QR dot in pixels.</param>
        /// <returns>A new <see cref="Texture2D"/>, or <c>null</c> on error.</returns>
        public static Texture2D GenerateQRTexture(string payload, int pixelsPerModule = 10)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                GamePeekConstants.LogError("QR payload cannot be null or empty.");
                return null;
            }

            try
            {
                using var generator = new global::QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(payload, global::QRCoder.QRCodeGenerator.ECCLevel.Q);
                using var code = new PngByteQRCode(data);

                byte[] pngBytes = code.GetGraphic(pixelsPerModule);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.LoadImage(pngBytes);
                return tex;
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogError($"QR generation failed: {ex.Message}");
                return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the custom-scheme connection payload.
        /// Format: <c>gamepeek://connect?ip=X&amp;port=Y&amp;name=MACHINE&amp;v=2</c>
        /// <para>
        /// iOS and Android recognise the <c>gamepeek://</c> scheme (registered in
        /// Info.plist / AndroidManifest) and open the GamePeek app directly without
        /// routing through a web browser. The <c>v</c> param carries the wire
        /// protocol version (PROTOCOL.md §2): a missing <c>v</c> means a v1 host,
        /// so the app can show "update the plugin" instead of failing silently.
        /// </para>
        /// </summary>
        private static string BuildPayload(string ip, int port)
        {
            string raw  = UnityEditor.EditorPrefs.GetString(GamePeekConstants.PrefEditorName, string.Empty);
            string name = Uri.EscapeDataString(string.IsNullOrWhiteSpace(raw) ? Environment.MachineName : raw);
            return $"gamepeek://connect?ip={ip}&port={port}&name={name}&v={GamePeekConstants.ProtoVersion}";
        }

        private static void DestroyCachedTexture()
        {
            if (_cachedTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(_cachedTexture);
                _cachedTexture = null;
            }
        }
    }
}
