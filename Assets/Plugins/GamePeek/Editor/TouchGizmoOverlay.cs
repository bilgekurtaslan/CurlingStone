using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GamePeek
{
    /// <summary>
    /// Draws touch-position circles on top of the Game View using GL whenever
    /// the phone sends touch events via GamePeek.
    /// Activated automatically via <see cref="InitializeOnLoadAttribute"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class TouchGizmoOverlay
    {
        private struct TouchPoint
        {
            public Vector2 NormalizedPos; // x=0 left, y=0 top
            public double  LastUpdated;   // EditorApplication.timeSinceStartup
        }

        private static readonly Dictionary<int, TouchPoint> _touches = new();
        private static Material _mat;
        private static Texture2D _touchTexture;
        private static GUIStyle _fingerLabelStyle;
        private static TouchGizmoOverlayBehaviour _gameViewBehaviour;

        /// <summary>
        /// Whether touch gizmos are drawn on the Game View.
        /// Persisted per-project via <see cref="EditorPrefs"/>.
        /// </summary>
        internal static bool ShowGizmos
        {
            get => EditorPrefs.GetBool(GamePeekConstants.PrefShowTouchGizmos, true);
            set => EditorPrefs.SetBool(GamePeekConstants.PrefShowTouchGizmos, value);
        }

        // How long (seconds) to keep showing a touch after the last "moved" before
        // auto-removing it (safety net in case "ended" is dropped by the network).
        private const double StaleTimeout = 2.0;

        static TouchGizmoOverlay()
        {
            GamePeekInput.OnTouchDetailed             += OnTouchDetailed;
            Camera.onPostRender                      += OnBuiltInPostRender;   // Built-in RP
            RenderPipelineManager.endFrameRendering  += OnSrpEndFrame;         // URP/HDRP
            EditorApplication.update                 += OnEditorUpdate;
            EditorApplication.playModeStateChanged   += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EnsureGameViewBehaviour();
        }

        // ── Domain reload cleanup ─────────────────────────────────────────────

        private static void OnBeforeAssemblyReload()
        {
            // HideAndDontSave objects survive domain reloads unless destroyed
            // explicitly — this class is [InitializeOnLoad], so without this the
            // texture and material would leak on every reload.
            if (_touchTexture != null) { Object.DestroyImmediate(_touchTexture); _touchTexture = null; }
            if (_mat          != null) { Object.DestroyImmediate(_mat);          _mat          = null; }
        }

        // ── Touch tracking ────────────────────────────────────────────────────

        private static void OnTouchDetailed(int fingerId, string phase, Vector2 normalizedPos)
        {
            if (phase == "ended" || phase == "canceled" || phase == "cancelled")
            {
                _touches.Remove(fingerId);
                return;
            }

            _touches[fingerId] = new TouchPoint
            {
                NormalizedPos = normalizedPos,
                LastUpdated   = EditorApplication.timeSinceStartup,
            };
        }

        // ── Editor update: stale cleanup + repaint in edit mode ───────────────

        private static void OnEditorUpdate()
        {
            if (_touches.Count == 0) return;

            double now = EditorApplication.timeSinceStartup;
            var toRemove = new List<int>();
            foreach (var kvp in _touches)
                if (now - kvp.Value.LastUpdated > StaleTimeout)
                    toRemove.Add(kvp.Key);
            foreach (var id in toRemove)
                _touches.Remove(id);

            // In edit mode the Game View doesn't repaint automatically.
            if (!Application.isPlaying)
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            else
                EnsureGameViewBehaviour();
        }

        // ── Render callbacks ──────────────────────────────────────────────────

        // Built-in RP: edit-mode fallback. In Play Mode the hidden OnGUI helper
        // draws directly into the Game View so the markers are part of the frame.
        private static void OnBuiltInPostRender(Camera cam)
        {
            if (Application.isPlaying) return;
            if (cam.cameraType != CameraType.Game) return;
            DrawOverlay();
        }

        // URP/HDRP: edit-mode fallback after the final screen blit.
        private static void OnSrpEndFrame(ScriptableRenderContext ctx, Camera[] cameras)
        {
            if (Application.isPlaying) return;
            bool hasGameCamera = false;
            foreach (var camera in cameras)
            {
                if (camera != null && camera.cameraType == CameraType.Game)
                {
                    hasGameCamera = true;
                    break;
                }
            }

            if (!hasGameCamera) return;
            DrawOverlay();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                EnsureGameViewBehaviour();
            else if (change == PlayModeStateChange.ExitingPlayMode ||
                     change == PlayModeStateChange.EnteredEditMode)
                DestroyGameViewBehaviour();
        }

        private static void EnsureGameViewBehaviour()
        {
            if (!Application.isPlaying || _gameViewBehaviour != null) return;

            var go = new GameObject("GamePeek Touch Overlay")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _gameViewBehaviour = go.AddComponent<TouchGizmoOverlayBehaviour>();
            Object.DontDestroyOnLoad(go);
        }

        private static void DestroyGameViewBehaviour()
        {
            if (_gameViewBehaviour == null) return;

            var go = _gameViewBehaviour.gameObject;
            _gameViewBehaviour = null;
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // ── Game View IMGUI draw ──────────────────────────────────────────────

        private static void DrawGameViewOverlay()
        {
            if (!ShowGizmos)         return;
            if (_touches.Count == 0) return;
            if (Event.current.type != EventType.Repaint) return;

            EnsureGuiResources();

            const float size = 52f;
            foreach (var kvp in _touches)
            {
                var tp = kvp.Value;
                float sx = tp.NormalizedPos.x * Screen.width;
                float sy = tp.NormalizedPos.y * Screen.height;
                var rect = new Rect(sx - size * 0.5f, sy - size * 0.5f, size, size);

                GUI.DrawTexture(rect, _touchTexture);
                GUI.Label(rect, Mathf.Clamp(kvp.Key % 10, 0, 9).ToString(), _fingerLabelStyle);
            }
        }

        private static void EnsureGuiResources()
        {
            if (_touchTexture == null)
            {
                const int texSize = 64;
                const float outerRadius = 30f;
                const float outlineWidth = 4f;
                var center = new Vector2((texSize - 1) * 0.5f, (texSize - 1) * 0.5f);
                _touchTexture = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                for (int y = 0; y < texSize; y++)
                {
                    for (int x = 0; x < texSize; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), center);
                        Color color = Color.clear;
                        if (d <= outerRadius)
                            color = new Color(1f, 0.35f, 0.25f, 0.45f);
                        if (d <= outerRadius && d >= outerRadius - outlineWidth)
                            color = new Color(1f, 1f, 1f, 0.90f);

                        _touchTexture.SetPixel(x, y, color);
                    }
                }

                _touchTexture.Apply();
            }

            if (_fingerLabelStyle == null)
            {
                _fingerLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold
                };
                _fingerLabelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.95f);
            }
        }

        private sealed class TouchGizmoOverlayBehaviour : MonoBehaviour
        {
            private void OnGUI() => DrawGameViewOverlay();
        }

        // ── GL draw ───────────────────────────────────────────────────────────

        private static void DrawOverlay()
        {
            if (!ShowGizmos)         return;
            if (_touches.Count == 0) return;

            EnsureMaterial();
            _mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadPixelMatrix(); // x: 0→Screen.width, y: 0(bottom)→Screen.height(top)

            foreach (var kvp in _touches)
            {
                var   tp  = kvp.Value;
                // Normalised y=0 is TOP; GL pixel matrix y=0 is BOTTOM — flip Y.
                float sx  = tp.NormalizedPos.x * Screen.width;
                float sy  = (1f - tp.NormalizedPos.y) * Screen.height;
                var   pos = new Vector2(sx, sy);

                DrawFilledCircle(pos, 26f, new Color(1f, 0.35f, 0.25f, 0.45f));
                DrawCircleOutline(pos, 26f, new Color(1f, 1f, 1f, 0.90f), 2.5f);
                DrawFingerIndex(pos, kvp.Key);
            }

            GL.PopMatrix();
        }

        // ── Finger index ──────────────────────────────────────────────────────

        private static void DrawFingerIndex(Vector2 center, int fingerId)
        {
            // Clamp to single digit for display. Finger IDs rarely exceed 9.
            int digit = Mathf.Clamp(fingerId % 10, 0, 9);
            // Digit bounding box: 7px wide × 12px tall, centered on the circle.
            const float dw = 7f, dh = 12f, dt = 1.5f;
            // topLeft.y is the TOP of the bounding box in GL coords (higher y = higher on screen).
            var topLeft = new Vector2(center.x - dw * 0.5f, center.y + dh * 0.5f);
            DrawSegmentDigit(topLeft, digit, dw, dh, dt, new Color(1f, 1f, 1f, 0.95f));
        }

        // ── 7-segment digit ───────────────────────────────────────────────────

        // Segment order: top(0), topLeft(1), topRight(2), middle(3),
        //                botLeft(4), botRight(5), bottom(6)
        private static readonly bool[][] s_DigitSegs =
        {
            new[] { true,  true,  true,  false, true,  true,  true  }, // 0
            new[] { false, false, true,  false, false, true,  false }, // 1
            new[] { true,  false, true,  true,  true,  false, true  }, // 2
            new[] { true,  false, true,  true,  false, true,  true  }, // 3
            new[] { false, true,  true,  true,  false, true,  false }, // 4
            new[] { true,  true,  false, true,  false, true,  true  }, // 5
            new[] { true,  true,  false, true,  true,  true,  true  }, // 6
            new[] { true,  false, true,  false, false, true,  false }, // 7
            new[] { true,  true,  true,  true,  true,  true,  true  }, // 8
            new[] { true,  true,  true,  true,  false, true,  true  }, // 9
        };

        /// <summary>
        /// Draws a 7-segment digit via GL.
        /// <paramref name="topLeft"/>.y is the TOP of the bounding box in GL pixel-matrix coords
        /// (y=0 bottom, y=Screen.height top), so top = highest y value.
        /// </summary>
        private static void DrawSegmentDigit(Vector2 topLeft, int digit, float w, float h, float t, Color c)
        {
            float x   = topLeft.x;
            float top = topLeft.y;        // top edge   (higher y)
            float bot = topLeft.y - h;    // bottom edge (lower y)
            float mid = topLeft.y - h * 0.5f;

            var segs = s_DigitSegs[digit];
            GL.Begin(GL.TRIANGLES);
            GL.Color(c);
            //              x            y                      w          h
            if (segs[0]) FillRect(x + t,       top - t,       w - 2*t,   t);                // top horiz
            if (segs[1]) FillRect(x,            mid,            t,         top - t - mid);   // top-left vert
            if (segs[2]) FillRect(x + w - t,   mid,            t,         top - t - mid);   // top-right vert
            if (segs[3]) FillRect(x + t,       mid - t*0.5f,  w - 2*t,   t);                // middle horiz
            if (segs[4]) FillRect(x,            bot + t,        t,         mid - t*0.5f - bot - t); // bot-left vert
            if (segs[5]) FillRect(x + w - t,   bot + t,        t,         mid - t*0.5f - bot - t); // bot-right vert
            if (segs[6]) FillRect(x + t,       bot,            w - 2*t,   t);                // bottom horiz
            GL.End();
        }

        // Fills a rect as two triangles. Must be called inside GL.Begin(TRIANGLES) / GL.End().
        private static void FillRect(float x, float y, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            GL.Vertex3(x,     y,     0);
            GL.Vertex3(x + w, y,     0);
            GL.Vertex3(x + w, y + h, 0);
            GL.Vertex3(x,     y,     0);
            GL.Vertex3(x + w, y + h, 0);
            GL.Vertex3(x,     y + h, 0);
        }

        // ── Primitives ────────────────────────────────────────────────────────

        private static void DrawFilledCircle(Vector2 center, float radius, Color color)
        {
            const int segments = 24;
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (int i = 0; i < segments; i++)
            {
                float a1 = i       * Mathf.PI * 2f / segments;
                float a2 = (i + 1) * Mathf.PI * 2f / segments;
                GL.Vertex3(center.x, center.y, 0);
                GL.Vertex3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, 0);
                GL.Vertex3(center.x + Mathf.Cos(a2) * radius, center.y + Mathf.Sin(a2) * radius, 0);
            }
            GL.End();
        }

        private static void DrawCircleOutline(Vector2 center, float radius, Color color, float thickness)
        {
            const int segments = 24;
            float r0 = radius - thickness;
            float r1 = radius;
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (int i = 0; i < segments; i++)
            {
                float a1  = i       * Mathf.PI * 2f / segments;
                float a2  = (i + 1) * Mathf.PI * 2f / segments;
                float cx1 = Mathf.Cos(a1), sy1 = Mathf.Sin(a1);
                float cx2 = Mathf.Cos(a2), sy2 = Mathf.Sin(a2);

                GL.Vertex3(center.x + cx1 * r0, center.y + sy1 * r0, 0);
                GL.Vertex3(center.x + cx1 * r1, center.y + sy1 * r1, 0);
                GL.Vertex3(center.x + cx2 * r1, center.y + sy2 * r1, 0);

                GL.Vertex3(center.x + cx1 * r0, center.y + sy1 * r0, 0);
                GL.Vertex3(center.x + cx2 * r1, center.y + sy2 * r1, 0);
                GL.Vertex3(center.x + cx2 * r0, center.y + sy2 * r0, 0);
            }
            GL.End();
        }

        private static void EnsureMaterial()
        {
            if (_mat != null) return;
            _mat = new Material(Shader.Find("Hidden/Internal-Colored"))
                { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite",   0);
            _mat.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.Always);
        }
    }
}
