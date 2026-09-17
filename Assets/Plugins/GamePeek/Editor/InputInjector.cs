// ENABLE_INPUT_SYSTEM alone is not enough: Unity defines it from Player
// Settings → Active Input Handling (New/Both) even when com.unity.inputsystem
// is not installed. ENABLE_INPUT_SYSTEM_PACKAGE comes from the asmdef's
// versionDefines and is only set when the package is actually present, so the
// Input System code paths compile only when both hold. (#define is per-file —
// this header is repeated in ConnectionManager.cs.)
#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
#define USE_INPUT_SYSTEM
#endif

using System;
using UnityEditor;
using UnityEngine;

#if USE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
#endif

namespace GamePeek
{
    /// <summary>
    /// Injects touch, gyroscope, and accelerometer events received from the
    /// companion app into Unity's Input systems.
    /// <para>
    /// Supports the <b>Legacy Input Manager</b>, the <b>new Input System package</b>,
    /// and Unity's <b>"Both"</b> active input handling mode.  When both backends
    /// are compiled in, touch is routed to the backend the active EventSystem
    /// input module consumes (both, when that cannot be determined) so UI events
    /// never double-fire.
    /// </para>
    /// <para>
    /// Gyroscope and accelerometer data is injected as virtual Input System
    /// sensor devices. The Legacy Input Manager has no injection hook for
    /// sensors (see the sensor notes in the legacy region below), so legacy-only
    /// projects consume sensor data through the <see cref="GamePeekInput"/>
    /// events, which are raised for every sample regardless of backend.
    /// </para>
    /// <para>
    /// All <c>Inject*</c> methods are safe to call from any thread; they
    /// internally marshal work to the Unity main thread where required.
    /// </para>
    /// </summary>
    public static class InputInjector
    {
        // ── Touch ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Injects a touch event into all active Input systems.
        /// </summary>
        /// <param name="phase">
        /// Touch phase string sent by the phone: <c>"began"</c>, <c>"moved"</c>,
        /// <c>"ended"</c>, or <c>"canceled"</c>.
        /// </param>
        /// <param name="normalizedX">Normalised X coordinate [0, 1] — left to right.</param>
        /// <param name="normalizedY">Normalised Y coordinate [0, 1] — top to bottom.</param>
        /// <param name="fingerId">Touch finger identifier (0-based).</param>
        public static void InjectTouch(string phase, float normalizedX, float normalizedY, int fingerId)
        {
            // Convert normalised → pixels in the actual game rendering resolution.
            // Screen.width/height is NOT safe here: this runs from the
            // EditorApplication.update queue drain right after the Game View was
            // focused, where Screen.* resolves to the focused window's client area
            // (toolbar included, scaled down when docked) — not the resolution the
            // streamed frame was captured at, so touches would land offset.
            GetGameRenderResolution(out float width, out float height);

            // Phone sends Y=0 at top; Unity uses Y=0 at bottom → flip Y.
            float screenX = Mathf.Clamp(normalizedX * width, 0f, width - 1f);
            float screenY = Mathf.Clamp((1f - normalizedY) * height, 0f, height - 1f);

#if USE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
            // Both backends compiled in — inject only into the one the active
            // EventSystem consumes, otherwise UI events double-fire.
            var backend = ResolveTouchBackend();
            if (backend != TouchBackend.Legacy)
                InjectTouchNewInputSystem(phase, screenX, screenY, fingerId);
            if (backend != TouchBackend.NewInputSystem)
                InjectTouchLegacy(phase, screenX, screenY, fingerId);
#elif USE_INPUT_SYSTEM
            InjectTouchNewInputSystem(phase, screenX, screenY, fingerId);
#elif ENABLE_LEGACY_INPUT_MANAGER
            InjectTouchLegacy(phase, screenX, screenY, fingerId);
#endif
        }

        // ── Rendering resolution ──────────────────────────────────────────────

        private static string _loggedResolutionBasis;
        private static System.Reflection.MethodInfo _getMainGameViewSize;
        private static bool _gameViewSizeResolved;

        /// <summary>
        /// Returns the resolution the game is actually rendering (and being
        /// captured) at, so normalised phone touches map 1:1 onto the frame.
        /// </summary>
        private static void GetGameRenderResolution(out float width, out float height)
        {
            // 1st choice: public PlayModeWindow API (2021.2+).
            try
            {
                PlayModeWindow.GetRenderingResolution(out uint w, out uint h);
                if (w > 0 && h > 0)
                {
                    width  = w;
                    height = h;
                    LogResolutionBasis("PlayModeWindow.GetRenderingResolution", width, height);
                    return;
                }
            }
            catch { /* fall through to reflection */ }

            // Fallback: internal Handles.GetMainGameViewSize via reflection.
            if (!_gameViewSizeResolved)
            {
                _gameViewSizeResolved = true;
                _getMainGameViewSize = typeof(Handles).GetMethod("GetMainGameViewSize",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static);
            }

            if (_getMainGameViewSize != null)
            {
                try
                {
                    var size = (Vector2)_getMainGameViewSize.Invoke(null, null);
                    if (size.x > 0f && size.y > 0f)
                    {
                        width  = size.x;
                        height = size.y;
                        LogResolutionBasis("Handles.GetMainGameViewSize (reflection)", width, height);
                        return;
                    }
                }
                catch { /* fall through to Screen */ }
            }

            // Last resort: Screen.* — may be the Game View window's client area
            // (toolbar included) rather than the true rendering resolution.
            width  = Screen.width;
            height = Screen.height;
            LogResolutionBasis("Screen.width/height (may include Game View chrome)", width, height);
        }

        // Logged only when the basis changes (never per-touch) — enough to make
        // touch-offset reports diagnosable remotely.
        private static void LogResolutionBasis(string basis, float width, float height)
        {
            if (basis == _loggedResolutionBasis) return;
            _loggedResolutionBasis = basis;
            GamePeekConstants.Log($"[InputInjector] Touch mapping resolution basis: {basis} ({width:F0}x{height:F0})");
        }

#if USE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
        // ── Backend selection (only when BOTH backends are compiled in) ───────

        // USE_INPUT_SYSTEM is set whenever the Input System package is installed
        // and Active Input Handling is New/Both — common in projects that still
        // use legacy input — so both paths are often compiled in even though only
        // one backend is active. Pick the backend the current EventSystem input
        // module consumes.

        private enum TouchBackend { Both, NewInputSystem, Legacy }

        // EventSystem lives in the UnityEngine.UI assembly, which this asmdef
        // does not reference — reach it via reflection.
        private static bool _eventSystemResolved;
        private static System.Reflection.PropertyInfo _eventSystemCurrentProp;
        private static System.Reflection.PropertyInfo _currentInputModuleProp;

        private static TouchBackend ResolveTouchBackend()
        {
            if (!_eventSystemResolved)
            {
                _eventSystemResolved = true;
                var esType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
                _eventSystemCurrentProp = esType?.GetProperty("current",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                _currentInputModuleProp = esType?.GetProperty("currentInputModule",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }

            object module = null;
            try
            {
                var eventSystem = _eventSystemCurrentProp?.GetValue(null);
                if (eventSystem != null)
                    module = _currentInputModuleProp?.GetValue(eventSystem);
            }
            catch { /* fall through to Both */ }

            if (IsInputSystemUIModule(module))
                return TouchBackend.NewInputSystem;
            if (module != null &&
                module.GetType().FullName == "UnityEngine.EventSystems.StandaloneInputModule")
                return TouchBackend.Legacy;

            // No EventSystem / unknown module → keep the historical both-paths behaviour.
            return TouchBackend.Both;
        }

        // InputSystemUIInputModule only exists when com.unity.ugui is installed
        // (Unity.InputSystem guards it behind a ugui versionDefine), so match by
        // name instead of a compile-time type reference.
        private static bool IsInputSystemUIModule(object module)
        {
            for (var t = module?.GetType(); t != null; t = t.BaseType)
                if (t.FullName == "UnityEngine.InputSystem.UI.InputSystemUIInputModule")
                    return true;
            return false;
        }
#endif

        // ── Gyroscope ─────────────────────────────────────────────────────────

        /// <summary>
        /// Injects gyroscope rotation-rate data.
        /// <para>
        /// Units: the phone (sensors_plus <c>GyroscopeEvent</c>) sends rad/s
        /// around each device axis — the same units and axes Unity uses for the
        /// Input System's <c>Gyroscope.angularVelocity</c> and legacy
        /// <c>Input.gyro.rotationRate</c> — so values pass through unconverted.
        /// </para>
        /// Also raises <see cref="GamePeekInput.OnGyro"/> on the main thread.
        /// </summary>
        public static void InjectGyro(float x, float y, float z)
        {
            var rotationRate = new Vector3(x, y, z);

#if USE_INPUT_SYSTEM
            InjectGyroNewInputSystem(rotationRate);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            InjectGyroLegacy();
#endif

            GamePeekInput.OnGyro?.Invoke(rotationRate);
        }

        // ── Accelerometer ─────────────────────────────────────────────────────

        // Standard gravity (m/s² per g), used to convert the phone's m/s²
        // readings into the g-multiples Unity expects. 9.80665 is the exact
        // conventional constant pinned by PROTOCOL.md §5.1 — and the value
        // sensors_plus uses on iOS to normalise CoreMotion readings to the
        // Android convention, so the round trip is loss-free on both platforms.
        private const float StandardGravity = 9.80665f;

        /// <summary>
        /// Injects accelerometer data.
        /// <para>
        /// Unit conversion: the phone (sensors_plus <c>AccelerometerEvent</c>)
        /// sends acceleration including gravity in m/s² with the Android sign
        /// convention — device flat on a table, screen up ⇒ (0, 0, +9.81); iOS
        /// readings are normalised to that same convention by sensors_plus.
        /// Unity — legacy <c>Input.acceleration</c> and the Input System's
        /// <c>Accelerometer.acceleration</c> alike — expects g-multiples with
        /// the opposite sign (flat, screen up ⇒ (0, 0, -1)), so dividing by
        /// -<see cref="StandardGravity"/> converts scale and sign in one step.
        /// </para>
        /// Also raises <see cref="GamePeekInput.OnAccel"/> on the main thread
        /// with the converted (g-multiples) value.
        /// </summary>
        public static void InjectAccelerometer(float x, float y, float z)
        {
            var acceleration = new Vector3(x, y, z) / -StandardGravity;

#if USE_INPUT_SYSTEM
            InjectAccelNewInputSystem(acceleration);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            InjectAccelLegacy();
#endif

            GamePeekInput.OnAccel?.Invoke(acceleration);
        }

        // ── New Input System paths ────────────────────────────────────────────

#if USE_INPUT_SYSTEM
        private static Touchscreen _touchscreen;
        // Fully qualified: a bare `Gyroscope` is ambiguous between the Input
        // System device and the legacy UnityEngine.Gyroscope type, both of which
        // this assembly references.
        private static UnityEngine.InputSystem.Gyroscope _gyroscope;
        private static Accelerometer _accelerometer;
        private static readonly System.Collections.Generic.HashSet<int> _activeTouchIds = new();

        /// <summary>
        /// Ensures synthetic virtual devices exist and are enabled.
        /// Call once from the main thread before the first injection.
        /// </summary>
        public static void EnsureVirtualDevices()
        {
            if (_touchscreen == null)
            {
                _touchscreen = InputSystem.GetDevice<Touchscreen>()
                    ?? InputSystem.AddDevice<Touchscreen>();
                InputSystem.EnableDevice(_touchscreen);
            }

            // Unlike Touchscreen, sensor devices start out DISABLED in the Input
            // System — EnableDevice after AddDevice is mandatory or the devices
            // never surface state to Gyroscope.current / Accelerometer.current.
            if (_gyroscope == null)
            {
                _gyroscope = InputSystem.GetDevice<UnityEngine.InputSystem.Gyroscope>()
                    ?? InputSystem.AddDevice<UnityEngine.InputSystem.Gyroscope>();
                InputSystem.EnableDevice(_gyroscope);
            }

            if (_accelerometer == null)
            {
                _accelerometer = InputSystem.GetDevice<Accelerometer>()
                    ?? InputSystem.AddDevice<Accelerometer>();
                InputSystem.EnableDevice(_accelerometer);
            }
        }

        /// <summary>Releases synthetic virtual devices on plugin shutdown.</summary>
        public static void RemoveVirtualDevices()
        {
            if (_touchscreen != null)   { InputSystem.RemoveDevice(_touchscreen);   _touchscreen   = null; }
            if (_gyroscope != null)     { InputSystem.RemoveDevice(_gyroscope);     _gyroscope     = null; }
            if (_accelerometer != null) { InputSystem.RemoveDevice(_accelerometer); _accelerometer = null; }
            _activeTouchIds.Clear();
        }

        private static void InjectTouchNewInputSystem(string phase, float screenX, float screenY, int fingerId)
        {
            if (_touchscreen == null) return;

            // Flutter uses "cancelled" (double-l); accept both spellings.
            UnityEngine.InputSystem.TouchPhase inputPhase = phase switch
            {
                "began"                   => UnityEngine.InputSystem.TouchPhase.Began,
                "moved"                   => UnityEngine.InputSystem.TouchPhase.Moved,
                "ended"                   => UnityEngine.InputSystem.TouchPhase.Ended,
                "canceled" or "cancelled" => UnityEngine.InputSystem.TouchPhase.Canceled,
                _                         => UnityEngine.InputSystem.TouchPhase.None,
            };

            if (inputPhase == UnityEngine.InputSystem.TouchPhase.None) return;

            int touchId = fingerId + 1;  // Unity touchId is 1-based
            var pos = new Vector2(screenX, screenY);

            bool isEnd = inputPhase == UnityEngine.InputSystem.TouchPhase.Ended ||
                         inputPhase == UnityEngine.InputSystem.TouchPhase.Canceled;

            // The phone app may skip "began"/"moved" and only send "ended" for quick taps.
            // The Input System ignores an "ended" with no prior "began", so synthesize one.
            if (isEnd && !_activeTouchIds.Contains(touchId))
            {
                _activeTouchIds.Add(touchId);
                InputSystem.QueueStateEvent(_touchscreen, new TouchState
                {
                    touchId  = touchId,
                    phase    = UnityEngine.InputSystem.TouchPhase.Began,
                    position = pos,
                });
            }

            // Track which touchIds are currently open.
            if (inputPhase == UnityEngine.InputSystem.TouchPhase.Began)
                _activeTouchIds.Add(touchId);
            else if (isEnd)
                _activeTouchIds.Remove(touchId);

            // Defer Ended/Canceled to the next editor frame.
            // InputSystemUIInputModule needs to process Began (→ PointerDown) in one
            // frame and Ended (→ PointerUp → onClick) in the next, otherwise both
            // events land in the same InputSystem.Update() call and the UI module
            // never establishes a pressed state before releasing it.
            if (isEnd)
            {
                var ts = _touchscreen;
                var id = touchId;
                var ph = inputPhase;
                var p  = pos;
                EditorApplication.delayCall += () =>
                {
                    if (ts == null || !ts.added) return;
                    InputSystem.QueueStateEvent(ts, new TouchState
                    {
                        touchId  = id,
                        phase    = ph,
                        position = p,
                    });
                };
                return;
            }

            InputSystem.QueueStateEvent(_touchscreen, new TouchState
            {
                touchId  = touchId,
                phase    = inputPhase,
                position = pos,
            });
        }

        private static void InjectGyroNewInputSystem(Vector3 rotationRate)
        {
            if (_gyroscope == null || !_gyroscope.added) return;
            // GyroscopeState is internal to Unity.InputSystem, so a full
            // InputSystem.QueueStateEvent is not possible from outside the
            // package. A delta state event against the angularVelocity control
            // is equivalent: that single Vector3 is the device's entire state
            // (format 'GYRO'). Value is rad/s, straight from the phone — no
            // conversion needed (see InjectGyro).
            InputSystem.QueueDeltaStateEvent(_gyroscope.angularVelocity, rotationRate);
        }

        private static void InjectAccelNewInputSystem(Vector3 acceleration)
        {
            if (_accelerometer == null || !_accelerometer.added) return;
            // AccelerometerState is likewise internal; the acceleration Vector3
            // is the device's entire state (format 'ACCL'). Value was already
            // converted from the phone's m/s² to Unity's g-multiples by
            // InjectAccelerometer.
            InputSystem.QueueDeltaStateEvent(_accelerometer.acceleration, acceleration);
        }
#endif

        // ── Legacy Input Manager paths ────────────────────────────────────────

#if ENABLE_LEGACY_INPUT_MANAGER
        // The Legacy Input Manager does not expose a public API for injecting
        // touch or sensor events at runtime. We use internal Unity reflection
        // to call the native method that fakes touch input. This is best-effort
        // and may break across Unity versions.

        private static bool _legacyWarningLogged;
        private static System.Reflection.MethodInfo _cachedSimMethod;
        private static bool _simMethodResolved;

        // Signature confirmed via diagnostics: SimulateTouch(Touch touch)
        private static System.Reflection.MethodInfo ResolveSimulateTouch()
        {
            if (_simMethodResolved) return _cachedSimMethod;
            _simMethodResolved = true;

            var flags = System.Reflection.BindingFlags.NonPublic
                      | System.Reflection.BindingFlags.Public
                      | System.Reflection.BindingFlags.Static;

            _cachedSimMethod = typeof(Input).GetMethod("SimulateTouch", flags, null,
                new[] { typeof(Touch) }, null);

            if (_cachedSimMethod == null)
                GamePeekConstants.LogWarning("[InputInjector] Input.SimulateTouch(Touch) not found.");

            return _cachedSimMethod;
        }

        private static void InjectTouchLegacy(string phase, float screenX, float screenY, int fingerId)
        {
            try
            {
                var touchPhase = phase switch
                {
                    "began"                   => UnityEngine.TouchPhase.Began,
                    "moved"                   => UnityEngine.TouchPhase.Moved,
                    "ended"                   => UnityEngine.TouchPhase.Ended,
                    "canceled" or "cancelled" => UnityEngine.TouchPhase.Canceled,
                    _                         => UnityEngine.TouchPhase.Stationary,
                };

                var simMethod = ResolveSimulateTouch();
                if (simMethod == null)
                {
                    if (!_legacyWarningLogged)
                    {
                        _legacyWarningLogged = true;
                        GamePeekConstants.LogWarning(
                            "[InputInjector] Touch injection unavailable in Legacy Input Manager for this Unity version.");
                    }
                    return;
                }

                var touch = new Touch
                {
                    fingerId = fingerId,
                    position = new Vector2(screenX, screenY),
                    phase    = touchPhase,
                };
                simMethod.Invoke(null, new object[] { touch });
            }
            catch (Exception ex)
            {
                if (!_legacyWarningLogged)
                {
                    _legacyWarningLogged = true;
                    GamePeekConstants.LogWarning($"[InputInjector] Legacy touch injection failed: {ex.Message}");
                }
            }
        }

        // ── Sensors (legacy) ──────────────────────────────────────────────────

        // Unlike touch, the Legacy Input Manager has NO injection hook for
        // sensor data — not even an internal one. A metadata scan of
        // UnityEngine.InputLegacyModule (2022.3) shows the only simulation
        // entry points are SimulateTouch/SimulateTouchInternal; the gyro and
        // accelerometer surface is read-only getters (GetGyroRotationRate,
        // get_acceleration, …) plus SetGyroEnabled/SetGyroUpdateInterval, which
        // configure the local hardware sensor rather than feed it data.
        // Input.gyro and Input.acceleration therefore always read the editor
        // machine's (non-existent) sensors and stay at zero — so instead of
        // silently dropping the data, tell the developer where to get it.

        private static bool _legacySensorNoticeLogged;

        private static void InjectGyroLegacy()  => LogLegacySensorNotice();

        private static void InjectAccelLegacy() => LogLegacySensorNotice();

        private static void LogLegacySensorNotice()
        {
            if (_legacySensorNoticeLogged) return;
            _legacySensorNoticeLogged = true;
#if USE_INPUT_SYSTEM
            // The new Input System is compiled in, so the data IS available via
            // the virtual sensor devices — informational only.
            GamePeekConstants.Log(
                "[InputInjector] Legacy Input.gyro / Input.acceleration cannot be simulated " +
                "(Unity has no sensor equivalent of Input.SimulateTouch); they will read zero. " +
                "Phone sensor data is injected into the new Input System instead — read " +
                "UnityEngine.InputSystem.Gyroscope.current / Accelerometer.current, or " +
                "subscribe to GamePeekInput.OnGyro / GamePeekInput.OnAccel.");
#else
            GamePeekConstants.LogWarning(
                "[InputInjector] Legacy Input.gyro / Input.acceleration cannot be simulated " +
                "(Unity has no sensor equivalent of Input.SimulateTouch); they will read zero. " +
                "To consume phone sensor data, install com.unity.inputsystem and read " +
                "Gyroscope.current / Accelerometer.current, or subscribe to " +
                "GamePeekInput.OnGyro / GamePeekInput.OnAccel.");
#endif
        }
#endif

    }
}
