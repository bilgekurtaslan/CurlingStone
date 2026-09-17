using System.IO;
using UnityEditor;
using UnityEngine;

namespace GamePeek
{
    internal sealed class GamePeekGuideWindow : EditorWindow
    {
        private const string ManualPath = "Assets/Plugins/GamePeek/MANUAL.md";

        private Vector2 _scrollPos;
        private string _manualText;
        private GUIStyle _headingStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _codeStyle;
        private bool _stylesInitialized;

        public static void Open()
        {
            var window = GetWindow<GamePeekGuideWindow>(utility: true, title: "GamePeek Guide", focus: true);
            window.minSize = new Vector2(420f, 520f);
            window.LoadManual();
        }

        private void OnEnable() => LoadManual();

        private void OnGUI()
        {
            InitStyles();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("GamePeek Guide", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Open Markdown", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    var manual = AssetDatabase.LoadAssetAtPath<TextAsset>(ManualPath);
                    if (manual != null)
                    {
                        Selection.activeObject = manual;
                        EditorGUIUtility.PingObject(manual);
                    }
                }

                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                    LoadManual();
            }

            if (string.IsNullOrEmpty(_manualText))
            {
                EditorGUILayout.HelpBox($"Manual not found at {ManualPath}.", MessageType.Warning);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            foreach (string rawLine in _manualText.Split('\n'))
                DrawMarkdownLine(rawLine.TrimEnd('\r'));
            EditorGUILayout.EndScrollView();
        }

        private void LoadManual()
        {
            _manualText = File.Exists(ManualPath)
                ? File.ReadAllText(ManualPath)
                : string.Empty;
        }

        private void DrawMarkdownLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                GUILayout.Space(8f);
                return;
            }

            if (line.StartsWith("#"))
            {
                string heading = line.TrimStart('#').Trim();
                GUILayout.Space(heading.Length > 0 ? 8f : 0f);
                GUILayout.Label(heading, _headingStyle);
                return;
            }

            if (line.StartsWith("```"))
            {
                GUILayout.Space(4f);
                return;
            }

            var style = line.StartsWith("    ") || line.StartsWith("|") ? _codeStyle : _bodyStyle;
            GUILayout.Label(CleanMarkdown(line), style);
        }

        private static string CleanMarkdown(string value)
        {
            return value
                .Replace("**", string.Empty)
                .Replace("`", string.Empty)
                .Replace("&amp;", "&");
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headingStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                wordWrap = true,
            };

            _bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                richText = false,
            };

            _codeStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                richText = false,
                padding = new RectOffset(12, 4, 2, 2),
            };
        }
    }
}
