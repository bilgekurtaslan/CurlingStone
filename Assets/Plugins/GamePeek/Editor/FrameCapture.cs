using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GamePeek
{
    /// <summary>Strategy used to capture each frame.</summary>
    public enum CaptureMethod
    {
        /// <summary>
        /// Blocking CPU readback. Edit Mode: <c>Camera.Render()</c> to a
        /// <see cref="RenderTexture"/> then <c>ReadPixels</c> (no Game View
        /// dependency). Play Mode: <c>ReadPixels</c> from the framebuffer after
        /// <c>WaitForEndOfFrame</c>.
        /// </summary>
        CameraRender,

        /// <summary>
        /// Non-blocking GPU readback via <c>AsyncGPUReadback.Request()</c> — no CPU
        /// stall — at the cost of approximately one frame of additional latency.
        /// Edit Mode: <c>Camera.Render()</c> to a <see cref="RenderTexture"/>
        /// (no Game View dependency). Play Mode:
        /// <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c> after
        /// <c>WaitForEndOfFrame</c>. Falls back to blocking <c>ReadPixels</c> in
        /// Play Mode when the platform does not support async GPU readback.
        /// </summary>
        AsyncGPUReadback,
    }

    /// <summary>
    /// Captures the composited Game View on the Unity main thread and forwards
    /// each frame to a <see cref="FrameEncoder"/>, which JPEG-encodes and
    /// broadcasts it on a background thread.
    /// <para>
    /// <b>Capture strategy:</b>
    /// <list type="bullet">
    ///   <item>In <b>Play mode</b>: a hidden <see cref="CaptureHelper"/> MonoBehaviour
    ///         runs a <c>WaitForEndOfFrame</c> coroutine, then either reads the
    ///         framebuffer with <c>ReadPixels</c>
    ///         (<see cref="CaptureMethod.CameraRender"/>) or copies it with
    ///         <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c> +
    ///         <c>AsyncGPUReadback</c> (<see cref="CaptureMethod.AsyncGPUReadback"/>).
    ///         Both capture the full Game View including UI, post-processing, and
    ///         overlays.</item>
    ///   <item>In <b>Edit mode</b>: renders the main camera directly to a
    ///         <see cref="RenderTexture"/> (Screen-space UI overlays are not
    ///         captured), read back blocking or async per <see cref="CaptureMethod"/>.</item>
    ///   <item>Frames are scaled to the target resolution via a GPU blit if the
    ///         captured size differs from <see cref="SetResolution"/>.</item>
    ///   <item>Readback textures and render textures are pooled and reused across
    ///         frames — recreated only when the resolution changes and destroyed in
    ///         <see cref="Stop"/> — so steady-state capture allocates nothing.</item>
    ///   <item>If the encoder is busy the frame is dropped immediately to avoid
    ///         main-thread stalls and unbounded queueing.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class FrameCapture
    {
        // ── Configuration ─────────────────────────────────────────────────────
        private int           _targetWidth;
        private int           _targetHeight;
        private float         _interval;    // seconds between captures = 1 / fpsCap
        private CaptureMethod _method = CaptureMethod.CameraRender;

        // ── State ─────────────────────────────────────────────────────────────
        private bool   _active;
        private double _lastCaptureTime;
        private bool   _hooked;
        private bool   _asyncRequestInFlight;

        // ── Pooled GPU/CPU resources (recreated only on size change) ──────────
        private Texture2D     _captureTex;   // target-size CPU readback target
        private Texture2D     _screenTex;    // screen-size CPU readback target (Play, blocking)
        private RenderTexture _screenRT;     // screen-size screenshot target (Play, async)

        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly FrameEncoder _encoder;

        // ── Play-mode overlay capture ─────────────────────────────────────────
        private CaptureHelper _helper;

        // ── Stats ─────────────────────────────────────────────────────────────
        private double _fpsWindowStart;
        private int    _fpsWindowCount;
        private float  _smoothedFps;

        /// <summary>Smoothed capture rate displayed in the Editor window (frames/second).</summary>
        public float SmoothedFps => _smoothedFps;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>Creates the capture component.</summary>
        /// <param name="encoder">Encoder to pass captured frames to.</param>
        /// <param name="targetWidth">Streaming width in pixels.</param>
        /// <param name="targetHeight">Streaming height in pixels.</param>
        /// <param name="fpsCap">Maximum capture rate (frames per second).</param>
        public FrameCapture(FrameEncoder encoder, int targetWidth, int targetHeight, int fpsCap)
        {
            _encoder      = encoder;
            _targetWidth  = targetWidth;
            _targetHeight = targetHeight;
            _interval     = fpsCap > 0 ? 1f / fpsCap : 1f / 30f;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Activates frame capture. Hooks into <see cref="EditorApplication.update"/>.</summary>
        public void Start()
        {
            if (_active) return;
            _active         = true;
            _fpsWindowStart = EditorApplication.timeSinceStartup;
            _fpsWindowCount       = 0;
            _smoothedFps          = 0f;
            _lastCaptureTime      = EditorApplication.timeSinceStartup - _interval;
            _asyncRequestInFlight = false;

            if (!_hooked)
            {
                EditorApplication.update += OnEditorUpdate;
                _hooked = true;
            }
        }

        /// <summary>
        /// Deactivates frame capture: unhooks from <see cref="EditorApplication.update"/>,
        /// destroys the pooled capture textures, and stops the encoder's background
        /// sender thread (<see cref="ConnectionManager"/> pairs each capture with
        /// one encoder and discards both together).
        /// </summary>
        public void Stop()
        {
            _active = false;
            if (_hooked)
            {
                EditorApplication.update -= OnEditorUpdate;
                _hooked = false;
            }
            DestroyHelper();
            ReleasePooledResources();
            _encoder?.Stop();
        }

        /// <summary>Updates the streaming resolution. Takes effect on the next capture.</summary>
        public void SetResolution(int width, int height)
        {
            _targetWidth  = width;
            _targetHeight = height;
        }

        /// <summary>Updates the FPS cap. Takes effect on the next capture.</summary>
        public void SetFpsCap(int fpsCap)
            => _interval = fpsCap > 0 ? 1f / fpsCap : 1f / 30f;

        /// <summary>Switches the capture strategy. Takes effect on the next capture.</summary>
        public void SetCaptureMethod(CaptureMethod method)
        {
            _method = method;
            if (method != CaptureMethod.AsyncGPUReadback)
                _asyncRequestInFlight = false;
        }

        // ── Editor update ─────────────────────────────────────────────────────

        private void OnEditorUpdate()
        {
            if (!_active) return;

            // Sync helper lifetime with Play mode.
            if (Application.isPlaying)
                EnsureHelper();
            else if (_helper != null)
                DestroyHelper();

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastCaptureTime < _interval) return;

            _lastCaptureTime = now;

            if (Application.isPlaying)
            {
                // CaptureHelper waits for WaitForEndOfFrame — when the framebuffer
                // includes Screen Space Overlay canvases and all post-processing —
                // then calls back into OnPlayModeEndOfFrame.
                if (_helper != null) _helper.RequestCapture();
            }
            else if (_method == CaptureMethod.AsyncGPUReadback)
            {
                CaptureFromCameraAsync();
            }
            else
            {
                CaptureFromCamera();
            }
        }

        // ── Play-mode helper management ───────────────────────────────────────

        private void EnsureHelper()
        {
            if (_helper != null) return;
            var go = new GameObject("[GamePeek] CaptureHelper") { hideFlags = HideFlags.HideAndDontSave };
            _helper              = go.AddComponent<CaptureHelper>();
            _helper.OnEndOfFrame = OnPlayModeEndOfFrame;
        }

        private void DestroyHelper()
        {
            if (_helper == null) return;
            if (_helper.gameObject != null)
                UnityEngine.Object.DestroyImmediate(_helper.gameObject);
            _helper = null;
            // The screenshot RT is only used by the Play-mode async path — don't
            // keep screen-sized VRAM around outside Play mode.
            ReleaseScreenRT();
        }

        /// <summary>
        /// Callback from <see cref="CaptureHelper"/> — runs on the main thread
        /// immediately after <c>WaitForEndOfFrame</c>, when the framebuffer holds
        /// the fully composited image.
        /// </summary>
        private void OnPlayModeEndOfFrame()
        {
            if (!_active || _encoder.IsEncoding) return;

            if (_method == CaptureMethod.AsyncGPUReadback && SystemInfo.supportsAsyncGPUReadback)
                CaptureScreenAsync();
            else
                CaptureScreenBlocking();
        }

        // ── Screen capture (Play mode, blocking) ──────────────────────────────

        /// <summary>
        /// Blocking Play-mode capture: <c>ReadPixels</c> from the screen
        /// framebuffer into a pooled texture, scaled to the target resolution via
        /// a GPU blit when required.
        /// </summary>
        private void CaptureScreenBlocking()
        {
            int screenW = Screen.width;
            int screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0) return;

            try
            {
                // The framebuffer stores the display-ready sRGB output; ReadPixels
                // copies the bytes as-is. The pooled texture is created non-linear
                // (see EnsurePooledTexture) so the encoder treats them as gamma
                // bytes and doesn't apply a second gamma pass.
                EnsurePooledTexture(ref _screenTex, screenW, screenH);
                _screenTex.ReadPixels(new Rect(0, 0, screenW, screenH), 0, 0);

                if (screenW == _targetWidth && screenH == _targetHeight)
                {
                    // No Apply() — the encoder only reads the CPU-side pixels.
                    if (_encoder.SubmitFrame(_screenTex)) UpdateFpsStats();
                    return;
                }

                // Scale on the GPU: upload the screen pixels, blit into a
                // target-sized sRGB RT, and read that back.
                _screenTex.Apply(false); // upload so the texture can be a blit source

                var rt = RenderTexture.GetTemporary(
                    _targetWidth, _targetHeight, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prevActive = RenderTexture.active;
                try
                {
                    Graphics.Blit(_screenTex, rt);
                    RenderTexture.active = rt;
                    // The sRGB RT already holds gamma-corrected bytes; ReadPixels
                    // copies them as-is (no sRGB→linear conversion).
                    EnsurePooledTexture(ref _captureTex, _targetWidth, _targetHeight);
                    _captureTex.ReadPixels(new Rect(0, 0, _targetWidth, _targetHeight), 0, 0);
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(rt);
                }

                if (_encoder.SubmitFrame(_captureTex)) UpdateFpsStats();
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Capture] Screen capture failed: {ex.Message}");
            }
        }

        // ── Screen capture (Play mode, non-blocking) ──────────────────────────

        /// <summary>
        /// Non-blocking Play-mode capture: copies the composited framebuffer into
        /// a reused screen-sized RenderTexture with
        /// <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c>, scales it to
        /// the target resolution on the GPU, then reads it back via
        /// <c>AsyncGPUReadback</c> — no CPU stall, ~1 frame of extra latency.
        /// </summary>
        private void CaptureScreenAsync()
        {
            if (_asyncRequestInFlight) return;

            int screenW = Screen.width;
            int screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0) return;

            RenderTexture rt = null;
            try
            {
                // CaptureScreenshotIntoRenderTexture requires the RT to match the
                // screen size exactly; the RT is pooled and only recreated when the
                // Game View is resized. sRGB read-write so the RT holds the same
                // display-ready gamma bytes as the framebuffer.
                EnsureScreenRT(screenW, screenH);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenRT);

                rt = RenderTexture.GetTemporary(
                    _targetWidth, _targetHeight, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                // The backbuffer copy is vertically flipped on graphics APIs whose
                // UV origin is the top (Direct3D/Metal/Vulkan) — undo it during the
                // scale blit so the encoded frame matches the ReadPixels path on
                // every platform.
                if (SystemInfo.graphicsUVStartsAtTop)
                    Graphics.Blit(_screenRT, rt, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else
                    Graphics.Blit(_screenRT, rt);
            }
            catch (Exception ex)
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                GamePeekConstants.LogWarning($"[Capture] Async screen capture failed: {ex.Message}");
                return;
            }

            _asyncRequestInFlight = true;
            RequestReadback(rt, _targetWidth, _targetHeight);
        }

        // ── Camera render (Edit mode, blocking) ───────────────────────────────

        private void CaptureFromCamera()
        {
            if (_encoder.IsEncoding) return;

            // Prefer Camera.main; fall back to any enabled camera
            var cam = Camera.main;
            if (cam == null)
            {
                var all = Camera.allCameras;
                cam = all.Length > 0 ? all[0] : null;
            }

            if (cam == null) return;

            RenderTexture rt         = null;
            RenderTexture prevActive = RenderTexture.active;
            var           prevTarget = cam.targetTexture;

            try
            {
                // sRGB read-write: prevents Graphics.Blit from linearising the
                // captured pixels in Linear-colour-space projects, which would
                // make colours appear washed-out on the phone.
                rt = RenderTexture.GetTemporary(
                    _targetWidth, _targetHeight, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                RenderTexture.active = rt;
                // The sRGB RT already holds gamma-corrected bytes; ReadPixels copies
                // them as-is (no sRGB→linear conversion) into the pooled non-linear
                // texture, so the encoder passes them through without a second
                // gamma pass that would whiten the image.
                EnsurePooledTexture(ref _captureTex, _targetWidth, _targetHeight);
                _captureTex.ReadPixels(new Rect(0, 0, _targetWidth, _targetHeight), 0, 0);
                RenderTexture.active = prevActive;

                if (_encoder.SubmitFrame(_captureTex)) UpdateFpsStats();
            }
            catch (Exception ex)
            {
                cam.targetTexture    = prevTarget;
                RenderTexture.active = prevActive;
                GamePeekConstants.LogWarning($"[Capture] Camera render failed: {ex.Message}");
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ── Camera → AsyncGPUReadback (Edit mode, non-blocking) ───────────────

        private void CaptureFromCameraAsync()
        {
            if (_asyncRequestInFlight || _encoder.IsEncoding) return;

            var cam = Camera.main;
            if (cam == null)
            {
                var all = Camera.allCameras;
                cam = all.Length > 0 ? all[0] : null;
            }
            if (cam == null) return;

            RenderTexture rt        = null;
            var           prevTarget = cam.targetTexture;

            try
            {
                rt = RenderTexture.GetTemporary(
                    _targetWidth, _targetHeight, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;
            }
            catch (Exception ex)
            {
                cam.targetTexture = prevTarget;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                GamePeekConstants.LogWarning($"[Capture] AsyncGPU camera render failed: {ex.Message}");
                return;
            }

            _asyncRequestInFlight = true;
            RequestReadback(rt, _targetWidth, _targetHeight);
        }

        // ── Shared async readback ─────────────────────────────────────────────

        /// <summary>
        /// Issues the non-blocking GPU→CPU readback shared by both async capture
        /// paths and forwards the raw bytes straight to the encoder — no
        /// intermediate <see cref="Texture2D"/> is created. The callback fires on
        /// the main thread; <paramref name="rt"/> is released when it completes.
        /// <paramref name="width"/>/<paramref name="height"/> are the dimensions
        /// the RT was created with (the target resolution may change before the
        /// callback fires).
        /// </summary>
        private void RequestReadback(RenderTexture rt, int width, int height)
        {
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGB24, req =>
            {
                RenderTexture.ReleaseTemporary(rt);
                _asyncRequestInFlight = false;

                if (req.hasError) return;
                if (!_active || _encoder.IsEncoding) return;

                try
                {
                    // AsyncGPUReadback returns raw sRGB bytes from the sRGB RT (no
                    // colour-space conversion). They go to the encoder as-is, which
                    // encodes them as sRGB data — no second gamma pass, no texture
                    // round-trip.
                    if (_encoder.SubmitFrame(req.GetData<byte>(), width, height))
                        UpdateFpsStats();
                }
                catch (Exception ex)
                {
                    GamePeekConstants.LogWarning($"[Capture] AsyncGPU readback processing failed: {ex.Message}");
                }
            });
        }

        // ── Pooled resource management ────────────────────────────────────────

        /// <summary>
        /// (Re)creates a pooled CPU readback texture when the required size
        /// changes. The texture is created non-linear (<c>linear: false</c>)
        /// because every capture path reads back display-ready gamma bytes — see
        /// the comments at each ReadPixels call site.
        /// </summary>
        private static void EnsurePooledTexture(ref Texture2D tex, int width, int height)
        {
            if (tex != null && tex.width == width && tex.height == height) return;
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            tex = new Texture2D(width, height, TextureFormat.RGB24, false, false)
            {
                // Survives play-mode transitions; destroyed explicitly in Stop().
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /// <summary>
        /// (Re)creates the pooled screen-sized RenderTexture used by the Play-mode
        /// async path when the Game View size changes.
        /// </summary>
        private void EnsureScreenRT(int width, int height)
        {
            if (_screenRT != null && _screenRT.width == width && _screenRT.height == height) return;
            ReleaseScreenRT();
            _screenRT = new RenderTexture(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _screenRT.Create();
        }

        private void ReleaseScreenRT()
        {
            if (_screenRT == null) return;
            _screenRT.Release();
            UnityEngine.Object.DestroyImmediate(_screenRT);
            _screenRT = null;
        }

        /// <summary>Destroys all pooled capture resources (streaming teardown).</summary>
        private void ReleasePooledResources()
        {
            if (_captureTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_captureTex);
                _captureTex = null;
            }
            if (_screenTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_screenTex);
                _screenTex = null;
            }
            ReleaseScreenRT();
        }

        // ── Stats ─────────────────────────────────────────────────────────────

        private void UpdateFpsStats()
        {
            _fpsWindowCount++;
            double elapsed = EditorApplication.timeSinceStartup - _fpsWindowStart;
            if (elapsed >= 1.0)
            {
                _smoothedFps    = (float)(_fpsWindowCount / elapsed);
                _fpsWindowCount = 0;
                _fpsWindowStart = EditorApplication.timeSinceStartup;
            }
        }
    }

    /// <summary>
    /// Hidden MonoBehaviour that schedules Play-mode capture at
    /// <c>WaitForEndOfFrame</c>, when the framebuffer holds the fully composited
    /// image (Screen Space Overlay canvases and all post-processing included).
    /// <see cref="OnEndOfFrame"/> is a zero-allocation callback —
    /// <see cref="FrameCapture"/> performs its own readback into pooled
    /// resources. Created and destroyed by its subscriber as needed.
    /// </summary>
    internal sealed class CaptureHelper : MonoBehaviour
    {
        /// <summary>
        /// Invoked on the main thread immediately after <c>WaitForEndOfFrame</c>.
        /// The subscriber performs its own (pooled) readback — nothing is
        /// allocated here.
        /// </summary>
        internal Action OnEndOfFrame;

        private bool _pending;

        /// <summary>Schedules one capture at the end of the current frame.</summary>
        internal void RequestCapture()
        {
            if (_pending) return;
            _pending = true;
            StartCoroutine(DoCaptureEndOfFrame());
        }

        private IEnumerator DoCaptureEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            _pending = false;

            try
            {
                OnEndOfFrame?.Invoke();
            }
            catch (Exception ex)
            {
                GamePeekConstants.LogWarning($"[Capture] End-of-frame capture threw: {ex.Message}");
            }
        }
    }
}
