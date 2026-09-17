using UnityEditor;
using UnityEngine;

namespace GamePeek
{
    /// <summary>
    /// Main GamePeek Editor window (<c>Window ▶ GamePeek</c>).
    /// </summary>
    public sealed class GamePeekWindow : EditorWindow
    {
        // ── Persistent settings ───────────────────────────────────────────────
        private bool _requirePlayMode;
        private bool _autoStartOnPlayMode;
        private LogLevel _logLevel;

        // Set when streaming was triggered automatically by entering Play Mode,
        // so we know to auto-stop it on exit.
        private bool _autoStartedByPlayMode;

        // Survives domain reloads; claimed with DeleteKey to prevent double-start.
        private const string PrefPendingStart = "GamePeek_PendingStart";

        // ── Runtime state ─────────────────────────────────────────────────────
        private bool  _streaming;
        private float _captureFps;
        private float _encodeMs;
        private float _rttMs; // phone-reported (STATS.rttMs); 0 until the first report

        // ── QR code ───────────────────────────────────────────────────────────
        private Texture2D _qrTexture;
        private Texture2D _downloadQrTexture;

        // ── Styles (initialized lazily) ───────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _versionStyle;
        private GUIStyle _statusTextStyle;
        private GUIStyle _sectionLabelStyle;
        private GUIStyle _infoIconStyle;
        private GUIStyle _tooltipBoxStyle;
        private bool     _stylesInitialized;

        // ── Assets ────────────────────────────────────────────────────────────
        private Texture2D _logoTexture;
        private Texture2D _proIcon;

        // ── Editor name ───────────────────────────────────────────────────────
        private string _editorName = string.Empty;

        // ── Port ──────────────────────────────────────────────────────────────
        private int _port = GamePeekConstants.DefaultPort;

        // ── Network interface selection ───────────────────────────────────────
        private int      _nicIndex;            // 0 = Auto, 1..n = specific interface
        private string[] _nicLabels = System.Array.Empty<string>();
        private string[] _nicIPs    = System.Array.Empty<string>();

        // ── Scroll ────────────────────────────────────────────────────────────
        private Vector2 _scrollPos;

        // ── Tooltip ───────────────────────────────────────────────────────────
        private string _hoveredTooltip;

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color ColGreen = new(0.18f, 0.80f, 0.32f);
        private static readonly Color ColAmber = new(1.00f, 0.72f, 0.00f);
        private static readonly Color ColGrey  = new(0.45f, 0.45f, 0.45f);

        // ─────────────────────────────────────────────────────────────────────
        // Menu item
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("Window/GamePeek")]
        public static void ShowWindow()
        {
            var logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Plugins/GamePeek/Textures/gamepeek-logo.png");

            var window = GetWindow<GamePeekWindow>(utility: false, title: "GamePeek", focus: true);
            window.titleContent = new GUIContent("GamePeek", logoTex, "GamePeek: Game View streaming");
            window.minSize = new Vector2(280f, 440f);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            LoadPrefs();
            RefreshNetworkInterfaces();
            SubscribeToManager();

            _logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Plugins/GamePeek/Textures/gamepeek-logo.png");
            _proIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Plugins/GamePeek/Textures/pro-user.png");
            _downloadQrTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Plugins/GamePeek/Textures/qr-code.png");

            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // The manager may already be streaming (window re-opened mid-stream, or
            // GamePeekSessionRestore restarted it after a domain reload) — mirror its
            // live state so the UI shows Stop/QR instead of a stale serialized value.
            _streaming = ConnectionManager.Instance.State != ConnectionState.Disconnected;
            if (_streaming) RefreshQR();

            // After a domain reload the EditorWindow is recreated while Unity is already
            // in play mode. EnteredPlayMode may have fired before OnEnable ran, so we
            // check the pending flag here as well — whichever runs first claims it.
            if (Application.isPlaying && EditorPrefs.GetBool(PrefPendingStart, false))
            {
                EditorPrefs.DeleteKey(PrefPendingStart);
                DoStartStreaming();
            }
            else if (!_requirePlayMode && EditorPrefs.GetBool(GamePeekConstants.PrefPersistStreaming, false))
            {
                DoStartStreaming();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromManager();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnFocus()
        {
            RefreshNetworkInterfaces();
            if (_streaming) RefreshQR();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(PrefPendingStart, false))
            {
                EditorPrefs.DeleteKey(PrefPendingStart);
                DoStartStreaming();
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode && _autoStartOnPlayMode && !_streaming)
            {
                DoStartStreaming();
                _autoStartedByPlayMode = true;
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode
                && !_requirePlayMode && !_streaming
                && EditorPrefs.GetBool(GamePeekConstants.PrefPersistStreaming, false))
            {
                DoStartStreaming();
                return;
            }

            if (_streaming && state == PlayModeStateChange.ExitingPlayMode
                && (_requirePlayMode || _autoStartedByPlayMode))
            {
                _autoStartedByPlayMode = false;
                StopStreaming();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GUI
        // ─────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            InitStyles();
            _hoveredTooltip = null;

            DrawHeader();

            using var scroll = new EditorGUILayout.ScrollViewScope(_scrollPos);
            _scrollPos = scroll.scrollPosition;

            GUILayout.Space(10f);
            DrawStatusCard();
            GUILayout.Space(10f);
            DrawMainButton();
            GUILayout.Space(8f);

            DrawQRCode();

            if (_streaming)
            {
                DrawStatsBar();
                GUILayout.Space(8f);
            }

            DrawSectionLabel("Options");
            DrawSettings();
            GUILayout.Space(8f);

            DrawDeviceList();

            if (!_streaming)
            {
                GUILayout.Space(8f);
                DrawDownloadAppCard();
            }

            GUILayout.FlexibleSpace();
            DrawFooter();
            DrawTooltipOverlay();
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            using var row = new EditorGUILayout.HorizontalScope(EditorStyles.toolbar);

            if (_logoTexture != null)
                GUILayout.Label(_logoTexture, GUILayout.Width(18f), GUILayout.Height(18f));

            GUILayout.Label("GamePeek", _titleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label($"v{GamePeekConstants.Version}", _versionStyle);
        }

        // ── Status card ───────────────────────────────────────────────────────

        private void DrawStatusCard()
        {
            var mgr   = ConnectionManager.Instance;
            var state = mgr.State;

            Color  dotColor;
            string primaryText;
            string secondaryText = string.Empty;

            switch (state)
            {
                case ConnectionState.Advertising:
                    dotColor      = ColAmber;
                    primaryText   = string.IsNullOrWhiteSpace(_editorName)
                        ? System.Environment.MachineName
                        : _editorName;
                    secondaryText = $"{QRCodeGenerator.GetLocalIPv4()}:{ConnectionManager.Instance.Port}";
                    break;
                case ConnectionState.Connected:
                    dotColor = ColGreen;
                    int n    = mgr.ConnectedDevices.Count;
                    primaryText   = n == 1 ? mgr.ConnectedDevices[0].DeviceName : $"{n} devices";
                    secondaryText = "Connected";
                    break;
                default:
                    dotColor    = ColGrey;
                    primaryText = "Not streaming";
                    break;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(6f);
                DrawColorDot(dotColor);
                GUILayout.Space(4f);
                GUILayout.Label(primaryText, _statusTextStyle, GUILayout.ExpandWidth(true));
                GUILayout.Space(6f);
            }

            if (!string.IsNullOrEmpty(secondaryText))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(28f);
                    GUILayout.Label(secondaryText, EditorStyles.miniLabel);
                }
            }

            GUILayout.Space(6f);
            EditorGUILayout.EndVertical();
        }

        // ── Main button ───────────────────────────────────────────────────────

        private void DrawMainButton()
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = _streaming
                ? new Color(0.88f, 0.30f, 0.30f)
                : new Color(0.28f, 0.76f, 0.44f);

            if (GUILayout.Button(
                    _streaming ? "■   Stop Streaming" : "▶   Start Streaming",
                    GUILayout.Height(42f)))
            {
                if (_streaming) StopStreaming();
                else            StartStreaming();
            }

            GUI.backgroundColor = prev;
        }

        // ── QR code ───────────────────────────────────────────────────────────

        private void DrawQRCode()
        {
            if (!_streaming)
            {
                DrawConnectionQRPlaceholder();
                return;
            }
            if (ConnectionManager.Instance.State == ConnectionState.Connected) return;

            RefreshQR();

            if (_qrTexture == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not generate QR code. Check the local network connection.",
                    MessageType.Warning);
                GUILayout.Space(6f);
                return;
            }

            float size = Mathf.Min(position.width - 48f, 220f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_qrTexture, GUILayout.Width(size), GUILayout.Height(size));
                GUILayout.FlexibleSpace();
            }

            GUILayout.Label(
                "Scan with the GamePeek app to connect",
                EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(10f);
        }

        /// <summary>
        /// Shown in place of the connection QR while not streaming, so users learn
        /// that the download QR below is not the one the in-app scanner expects.
        /// </summary>
        private void DrawConnectionQRPlaceholder()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(12f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Connection QR", _statusTextStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Label(
                "Appears here when you press  ▶ Start Streaming",
                EditorStyles.centeredGreyMiniLabel);

            GUILayout.Space(12f);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        // ── Download app card ─────────────────────────────────────────────────

        private void DrawDownloadAppCard()
        {
            DrawSectionLabel("Get the Mobile App");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(8f);

            GUILayout.Label("Get the mobile app", _statusTextStyle);
            GUILayout.Label(
                "This QR code is the App Store / Google Play DOWNLOAD link. Scan it with " +
                "your phone's camera to install the GamePeek app. It is not a connection code.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8f);

            if (_downloadQrTexture != null)
            {
                float size = Mathf.Min(position.width - 72f, 180f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(_downloadQrTexture, GUILayout.Width(size), GUILayout.Height(size));
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Download QR texture not found at Assets/Plugins/GamePeek/Textures/qr-code.png.",
                    MessageType.Warning);
            }

            GUILayout.Space(6f);
            GUILayout.Label(
                "The connection QR appears in this window after you press  ▶ Start Streaming.",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(6f);
            if (GUILayout.Button("Open GamePeek App Page", GUILayout.Height(26f)))
                Application.OpenURL("https://gamepeek.app");

            GUILayout.Space(6f);
            EditorGUILayout.EndVertical();
        }

        // ── Stats bar ─────────────────────────────────────────────────────────

        private void DrawStatsBar()
        {
            using var row = new EditorGUILayout.HorizontalScope(EditorStyles.helpBox);

            GUILayout.Label($"FPS  {_captureFps:F1}", EditorStyles.miniLabel, GUILayout.Width(64f));
            GUILayout.Label($"Enc  {_encodeMs:F0} ms", EditorStyles.miniLabel, GUILayout.Width(70f));

            // Phone-reported RTT (STATS.rttMs, 1 Hz) — shown once the first
            // report arrives.
            if (_rttMs > 0f)
            {
                DrawColorDot(RttColor(_rttMs), small: true);
                GUILayout.Label($"RTT {_rttMs:F0} ms", EditorStyles.miniLabel, GUILayout.Width(70f));
            }

            GUILayout.FlexibleSpace();
            int c = ConnectionManager.Instance.ConnectedDevices.Count;
            GUILayout.Label(c == 1 ? "1 client" : $"{c} clients", EditorStyles.miniLabel);
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void DrawSettings()
        {
            // ── Editor Name ────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Editor Name",
                    "Display name shown in the GamePeek app's device list.\nDefaults to your machine name.");
                _editorName = EditorGUILayout.TextField(_editorName, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Set", EditorStyles.miniButton, GUILayout.Width(32f)))
                {
                    SavePrefs();
                    QRCodeGenerator.Invalidate();
                }
            }

            // ── Run in Play Mode ───────────────────────────────────────────────
            int pmIndex;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Run in Play Mode",
                    "On: streaming only runs while the Editor is in Play Mode.\nOff: streams in Edit + Play Mode (briefly drops on script recompile).");
                pmIndex = EditorGUILayout.Popup(_requirePlayMode ? 0 : 1,
                    new[] { "True", "False" }, GUILayout.ExpandWidth(true));
            }
            if (EditorGUI.EndChangeCheck())
            {
                _requirePlayMode = pmIndex == 0;
                SavePrefs();
            }
            if (!_requirePlayMode)
            {
                GUILayout.Space(2f);
                EditorGUILayout.HelpBox(
                    "Recompiling will cut the connection. Pro users have automatic reconnect. " +
                    "Enabling 'Run in Play Mode' avoids interruptions.",
                    MessageType.Info);
            }

            // ── Auto Start on Play Mode ────────────────────────────────────────
            GUILayout.Space(4f);
            bool newAutoStart;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Auto Start on Play Mode",
                    "Automatically starts streaming when entering Play Mode and stops when exiting.");
                newAutoStart = EditorGUILayout.Toggle(_autoStartOnPlayMode, GUILayout.ExpandWidth(true));
            }
            if (EditorGUI.EndChangeCheck())
            {
                _autoStartOnPlayMode = newAutoStart;
                SavePrefs();
            }

            // ── Log Level ──────────────────────────────────────────────────────
            GUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Log Level",
                    "Controls how much GamePeek writes to the Unity Console.\nNone: silent  |  Error: errors only  |  Warning: errors + warnings  |  All: full diagnostics.");
                _logLevel = (LogLevel)EditorGUILayout.EnumPopup(_logLevel, GUILayout.ExpandWidth(true));
            }
            if (EditorGUI.EndChangeCheck())
            {
                GamePeekConstants.CurrentLogLevel = _logLevel;
                SavePrefs();
            }

            // ── Network Interface ──────────────────────────────────────────────
            GUILayout.Space(4f);
            DrawNetworkInterfaceDropdown();

            // ── Capture Method ─────────────────────────────────────────────────
            GUILayout.Space(4f);
            var mgr = ConnectionManager.Instance;
            CaptureMethod newMethod;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Capture Method",
                    "Camera Render: synchronous, works in Edit + Play Mode.\nAsync GPU Readback: non-blocking, ~1 frame of extra latency.");
                newMethod = (CaptureMethod)EditorGUILayout.EnumPopup(mgr.ActiveCaptureMethod, GUILayout.ExpandWidth(true));
            }
            if (EditorGUI.EndChangeCheck())
                mgr.SetCaptureMethod(newMethod);

            // ── Touch Gizmos ───────────────────────────────────────────────────
            GUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Show Touch Gizmos",
                    "Draws touch-position circles on the Game View when the phone sends touch events.");
                EditorGUILayout.Toggle(TouchGizmoOverlay.ShowGizmos, GUILayout.ExpandWidth(true));
            }
            if (EditorGUI.EndChangeCheck())
                TouchGizmoOverlay.ShowGizmos = !TouchGizmoOverlay.ShowGizmos;
        }

        // ── Network interface dropdown ────────────────────────────────────────

        private void RefreshNetworkInterfaces()
        {
            var candidates = NetworkInterfaceSelector.GetCandidates();

            _nicLabels    = new string[candidates.Count + 1];
            _nicIPs       = new string[candidates.Count];
            _nicLabels[0] = "Auto (best match)";

            for (int i = 0; i < candidates.Count; i++)
            {
                _nicLabels[i + 1] = candidates[i].Label;
                _nicIPs[i]        = candidates[i].IP;
            }

            // Restore saved selection (reset to Auto if the saved IP is no longer present).
            string savedIP = NetworkInterfaceSelector.GetSavedIP();
            _nicIndex = 0;
            if (!string.IsNullOrEmpty(savedIP))
            {
                bool savedIpFound = false;
                for (int i = 0; i < _nicIPs.Length; i++)
                {
                    if (_nicIPs[i] == savedIP)
                    {
                        _nicIndex = i + 1;
                        savedIpFound = true;
                        break;
                    }
                }

                if (!savedIpFound)
                {
                    NetworkInterfaceSelector.SaveIP(string.Empty);
                    QRCodeGenerator.Invalidate();
                }
            }
        }

        private void DrawNetworkInterfaceDropdown()
        {
            int newIndex;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                OptionLabel("Network Interface",
                    "Which network adapter GamePeek binds to.\nUse Auto for most setups. Change this if GamePeek advertises a virtual adapter (e.g. VPN or Hyper-V).");
                newIndex = EditorGUILayout.Popup(_nicIndex, _nicLabels, GUILayout.ExpandWidth(true));
            }
            if (EditorGUI.EndChangeCheck())
            {
                _nicIndex = newIndex;
                string selectedIP = _nicIndex == 0 ? string.Empty : _nicIPs[_nicIndex - 1];
                NetworkInterfaceSelector.SaveIP(selectedIP);
                QRCodeGenerator.Invalidate();

                if (_streaming)
                {
                    StopStreaming();
                    DoStartStreaming();
                }
            }

            if (_nicIndex == 0)
            {
                string bestIp = NetworkInterfaceSelector.GetBestIP();
                EditorGUILayout.HelpBox(
                    $"Auto-selected: {bestIp}. Change this if GamePeek advertises a virtual adapter address.",
                    MessageType.None);
            }
        }

        // ── Device list ───────────────────────────────────────────────────────

        private void DrawDeviceList()
        {
            var devices = ConnectionManager.Instance.ConnectedDevices;
            if (devices.Count == 0) return;

            DrawSectionLabel("Connected Devices");

            int toDisconnect = -1;
            foreach (var d in devices)
            {
                using var card = new EditorGUILayout.HorizontalScope(EditorStyles.helpBox);
                DrawColorDot(ColGreen);
                GUILayout.Space(2f);
                if (d.IsPro && _proIcon != null)
                    GUILayout.Label(_proIcon, GUILayout.Width(16f), GUILayout.Height(16f));
                GUILayout.Label(d.DeviceName, GUILayout.ExpandWidth(true));
                GUILayout.Label(
                    d.ConnectedAt.ToLocalTime().ToString("HH:mm:ss"),
                    EditorStyles.miniLabel, GUILayout.Width(52f));
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f)))
                    toDisconnect = d.SessionId;
            }

            if (toDisconnect >= 0)
                ConnectionManager.Instance.DisconnectDevice(toDisconnect);

            GUILayout.Space(8f);
        }


        // ── Footer ────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            GUILayout.Label(
                "Pro features (multi-device, 1080p, 60 fps) unlocked via the GamePeek app.",
                EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(2f);

            using var row = new EditorGUILayout.HorizontalScope(EditorStyles.toolbar);
            GUILayout.Label("Port", EditorStyles.miniLabel, GUILayout.Width(28f));
            EditorGUI.BeginDisabledGroup(_streaming);
            // While streaming, show the port actually bound (StartStreaming may
            // have fallen back to a nearby port); otherwise show the saved setting.
            int displayPort = _streaming ? ConnectionManager.Instance.Port : _port;
            var newPort = EditorGUILayout.IntField(displayPort, EditorStyles.toolbarTextField, GUILayout.Width(50f));
            if (!_streaming && newPort != _port && newPort > 1024 && newPort <= 65535)
            {
                _port = newPort;
                SavePrefs();
                QRCodeGenerator.Invalidate();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Welcome", EditorStyles.toolbarButton, GUILayout.Width(62f)))
                GamePeekWelcomeWindow.Open();

            if (GUILayout.Button("Guide", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                GamePeekGuideWindow.Open();

            if (GUILayout.Button("Docs", EditorStyles.toolbarButton, GUILayout.Width(38f)))
                Application.OpenURL("https://gamepeek.app");

            if (GUILayout.Button("Reset FW", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                FirewallHelper.ResetAndReConfigure();
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private void DrawSectionLabel(string title)
        {
            GUILayout.Space(2f);
            using var row = new EditorGUILayout.HorizontalScope();
            GUILayout.Label(title.ToUpper(), _sectionLabelStyle);
            GUILayout.Space(4f);
        }

        /// <summary>
        /// Draws a label column with a trailing (i) icon. Hover is detected manually
        /// so Unity's native tooltip system is never triggered (avoids double tooltip).
        /// </summary>
        private void OptionLabel(string label, string tooltip)
        {
            GUILayout.Label(label, EditorStyles.label,
                GUILayout.Width(EditorGUIUtility.labelWidth - 18f));
            // No tooltip in GUIContent — we track hover ourselves to avoid Unity's native tooltip.
            GUILayout.Label(new GUIContent("ⓘ"), _infoIconStyle,
                GUILayout.Width(16f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (Event.current.type == EventType.Repaint &&
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                _hoveredTooltip = tooltip;
            }
            GUILayout.Space(2f);
        }

        /// <summary>
        /// Draws a floating tooltip box near the mouse when hovering over an (i) icon.
        /// Must be called at the very end of OnGUI.
        /// </summary>
        private void DrawTooltipOverlay()
        {
            if (string.IsNullOrEmpty(_hoveredTooltip)) return;

            var   content = new GUIContent(_hoveredTooltip);
            float maxW    = Mathf.Min(position.width - 24f, 260f);
            float textH   = _tooltipBoxStyle.CalcHeight(content, maxW - 16f);
            float height  = textH + 14f;
            var   mp      = Event.current.mousePosition;

            var rect = new Rect(mp.x + 16f, mp.y - height - 6f, maxW, height);
            rect.x = Mathf.Clamp(rect.x, 4f, position.width  - maxW   - 4f);
            rect.y = Mathf.Clamp(rect.y, 4f, position.height - height - 4f);

            // Solid background — no transparency
            var bgColor  = EditorGUIUtility.isProSkin
                ? new Color(0.15f, 0.15f, 0.15f, 1f)
                : new Color(0.90f, 0.90f, 0.90f, 1f);
            var rimColor = EditorGUIUtility.isProSkin
                ? new Color(0.40f, 0.40f, 0.40f, 1f)
                : new Color(0.60f, 0.60f, 0.60f, 1f);

            EditorGUI.DrawRect(rect, rimColor);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), bgColor);

            var textRect = new Rect(rect.x + 8f, rect.y + 7f, rect.width - 16f, textH);
            GUI.Label(textRect, _hoveredTooltip, _tooltipBoxStyle);

            Repaint();
        }

        private static void DrawColorDot(Color color, bool small = false)
        {
            float w = small ? 14f : 16f;
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label("●", GUILayout.Width(w), GUILayout.Height(w));
            GUI.color = prev;
        }

        private static Color RttColor(float rttMs)
        {
            if (rttMs <= 0f)                          return ColGrey;
            if (rttMs < GamePeekConstants.RttGreenMs)  return ColGreen;
            if (rttMs < GamePeekConstants.RttYellowMs) return new Color(1f, 0.9f, 0f);
            if (rttMs < GamePeekConstants.RttOrangeMs) return new Color(1f, 0.5f, 0f);
            return new Color(0.9f, 0.2f, 0.2f);
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft,
            };

            _versionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = new Color(0.55f, 0.55f, 0.55f) },
            };

            _statusTextStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize  = 12,
                fontStyle = FontStyle.Bold,
            };

            _sectionLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.50f, 0.50f, 0.50f) },
            };

            _infoIconStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(1, 1, 0, 1),
                normal    = { textColor = new Color(0.40f, 0.75f, 1.00f) },
            };

            _tooltipBoxStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal   = { textColor = EditorGUIUtility.isProSkin ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.10f, 0.10f, 0.10f) },
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Streaming control
        // ─────────────────────────────────────────────────────────────────────

        private void StartStreaming()
        {
            if (_streaming) return;

            SavePrefs();
            Application.runInBackground = true;

            if (_requirePlayMode && !EditorApplication.isPlaying)
            {
                // Setting isPlaying triggers a domain reload — plant a flag so streaming
                // resumes once the domain is back up (EnteredPlayMode / OnEnable).
                EditorPrefs.SetBool(PrefPendingStart, true);
                EditorApplication.isPlaying = true;
                return;
            }

            DoStartStreaming();
        }

        private void DoStartStreaming()
        {
            if (!ConnectionManager.Instance.StartStreaming(_port))
            {
                _streaming = false;
                EditorPrefs.DeleteKey(PrefPendingStart);
                EditorPrefs.DeleteKey(GamePeekConstants.PrefPersistStreaming);
                Repaint();
                return;
            }

            _streaming = true;

            if (!_requirePlayMode)
                EditorPrefs.SetBool(GamePeekConstants.PrefPersistStreaming, true);

            Application.runInBackground = true;
            RefreshQR();
            Repaint();
        }

        private void StopStreaming()
        {
            if (!_streaming) return;
            ConnectionManager.Instance.StopStreaming();
            _streaming             = false;
            _autoStartedByPlayMode = false;
            _captureFps            = 0f;
            _encodeMs              = 0f;
            EditorPrefs.DeleteKey(GamePeekConstants.PrefPersistStreaming);
            DestroyQR();
            Repaint();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ConnectionManager subscriptions
        // ─────────────────────────────────────────────────────────────────────

        private void SubscribeToManager()
        {
            var mgr = ConnectionManager.Instance;
            mgr.StateChanged       += OnStateChanged;
            mgr.DeviceConnected    += OnDeviceConnected;
            mgr.DeviceDisconnected += OnDeviceDisconnected;
            mgr.StatsUpdated       += OnStatsUpdated;
            mgr.RttUpdated         += OnRttUpdated;
        }

        private void UnsubscribeFromManager()
        {
            if (ConnectionManager.Instance == null) return;
            var mgr = ConnectionManager.Instance;
            mgr.StateChanged       -= OnStateChanged;
            mgr.DeviceConnected    -= OnDeviceConnected;
            mgr.DeviceDisconnected -= OnDeviceDisconnected;
            mgr.StatsUpdated       -= OnStatsUpdated;
            mgr.RttUpdated         -= OnRttUpdated;
        }

        private void OnStateChanged(ConnectionState state)
        {
            // Streaming can start/stop outside this window (GamePeekSessionRestore
            // after a domain reload, API calls) — keep the button and QR in sync.
            bool streaming = state != ConnectionState.Disconnected;
            if (streaming != _streaming)
            {
                _streaming = streaming;
                if (streaming) RefreshQR();
                else           DestroyQR();
            }
            Repaint();
        }

        private void OnDeviceConnected(DeviceInfo _)    => Repaint();
        private void OnDeviceDisconnected(DeviceInfo _) => Repaint();

        private void OnStatsUpdated(float fps, float encodeMs)
        {
            _captureFps = fps;
            _encodeMs   = encodeMs;
            Repaint();
        }

        private void OnRttUpdated(float rttMs)
        {
            _rttMs = rttMs;
            Repaint();
        }

        // ─────────────────────────────────────────────────────────────────────
        // QR helpers
        // ─────────────────────────────────────────────────────────────────────

        // Uses the manager's Port (the port actually bound) rather than _port —
        // StartStreaming may have fallen back to port+1…+9 when the requested
        // port was taken. Only called while streaming, so Port is always live.
        private void RefreshQR()
            => _qrTexture = QRCodeGenerator.GetConnectionQR(
                ConnectionManager.Instance.Port, pixelsPerModule: 8);

        private void DestroyQR() => _qrTexture = null;

        // ─────────────────────────────────────────────────────────────────────
        // EditorPrefs + crash-safe file storage
        // ─────────────────────────────────────────────────────────────────────

        // EditorPrefs (NSUserDefaults on macOS) has a delayed disk flush — a hard
        // crash can lose the last write. The editor name is also written to a file
        // so it survives crashes (file I/O is synchronous / immediately flushed).
        private static readonly string EditorNameFilePath =
            System.IO.Path.Combine("UserSettings", "GamePeekEditorName.txt");

        private void LoadPrefs()
        {
            _requirePlayMode     = EditorPrefs.GetBool(GamePeekConstants.PrefAutoStopPlay, true);
            _autoStartOnPlayMode = EditorPrefs.GetBool(GamePeekConstants.PrefAutoStartOnPlay, false);
            _logLevel            = (LogLevel)EditorPrefs.GetInt(GamePeekConstants.PrefLogLevel, (int)LogLevel.All);
            _port                = EditorPrefs.GetInt(GamePeekConstants.PrefPort, GamePeekConstants.DefaultPort);
            GamePeekConstants.CurrentLogLevel = _logLevel;

            // File takes priority — it's written synchronously so it's crash-safe.
            if (System.IO.File.Exists(EditorNameFilePath))
                _editorName = System.IO.File.ReadAllText(EditorNameFilePath);
            else
                _editorName = EditorPrefs.GetString(GamePeekConstants.PrefEditorName, string.Empty);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(GamePeekConstants.PrefAutoStopPlay, _requirePlayMode);
            EditorPrefs.SetBool(GamePeekConstants.PrefAutoStartOnPlay, _autoStartOnPlayMode);
            EditorPrefs.SetInt(GamePeekConstants.PrefLogLevel, (int)_logLevel);
            EditorPrefs.SetString(GamePeekConstants.PrefEditorName, _editorName);
            EditorPrefs.SetInt(GamePeekConstants.PrefPort, _port);

            // Also write to file for crash resilience.
            try
            {
                System.IO.Directory.CreateDirectory("UserSettings");
                System.IO.File.WriteAllText(EditorNameFilePath, _editorName);
            }
            catch { /* non-critical */ }
        }
    }
}
