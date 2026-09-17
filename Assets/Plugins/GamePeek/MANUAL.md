# GamePeek User Manual

Stream your Unity Game View live to your iOS or Android phone over Wi-Fi.

---

## What You Need

- A phone with the **GamePeek** companion app installed (iOS or Android)
- Both your PC/Mac and phone on the **same Wi-Fi network**
- Unity 2021.3 or newer

---

## Opening the GamePeek Window

Go to **Window → GamePeek** in the Unity menu bar.

The GamePeek window can be docked anywhere in your editor layout like any other Unity panel.

---

## Connecting Your Phone

### Option 1: QR Code (easiest)

1. Click **Start Streaming** in the GamePeek window.
2. A QR code appears in the window.
3. Open the GamePeek app on your phone and tap **Scan QR**.
4. Point the camera at the QR code.
5. The dot in the window turns green; you are connected.

### Option 2: Auto-Discovery (mDNS)

GamePeek broadcasts its presence on the local network automatically.
In the GamePeek app, tap **Browse** and your machine name will appear in the list. Tap it to connect.

No QR code needed. Both devices must be on the same subnet.

### Option 3: Remote play via Tailscale

Streaming outside your Wi-Fi (another room, another city) works through [Tailscale](https://tailscale.com), a free zero-config VPN:

1. Install Tailscale on both the editor machine and the phone, and sign both into the same tailnet.
2. Click **Start Streaming** in the GamePeek window.
3. In the GamePeek app, choose **Connect by IP** and enter the editor machine's tailnet IP (shown in the Tailscale app, looks like `100.x.y.z`) and the GamePeek port.

The QR code and Browse discovery only work on the local network; manual IP entry is the expected flow for remote play. On iOS, tailnet connections do not need the Local Network permission.

---

## The Editor Window at a Glance

| Area | What it does |
| --- | --- |
| Status dot | Grey = stopped, Amber = waiting for phone, Green = connected |
| **Start / Stop Streaming** button | Starts or stops the stream |
| QR code | Appears while waiting; disappears once connected |
| Stats bar | Shows live FPS, encode time, and round-trip latency reported by the phone |
| **Options** section | Editor name, play mode behaviour, capture method, log level |
| **Connected Devices** list | Shows all currently connected phones |
| **Docs** button | Opens this manual online |
| **Reset FW** button | Re-runs Windows Firewall setup if connections are failing |
| **Port** field | Configurable listen port (default 7777); editable only when not streaming |

---

## Options

### Editor Name

Sets the display name shown in the GamePeek app's device list. Defaults to your machine name.
Type a name and click **Set** to save it.

### Run in Play Mode

When **on**: streaming only runs while the Editor is in Play Mode. Entering Edit Mode automatically stops streaming.

When **off**: streaming runs all the time, even in Edit Mode. The stream will briefly drop when scripts recompile (domain reload). Useful for inspecting the editor camera without entering Play.

### Capture Method

| Method | Description |
| --- | --- |
| **Camera Render** | Renders the main camera synchronously. Works in Edit and Play Mode. |
| **Async GPU Readback** | Same render path but non-blocking; reduces CPU stall at the cost of ~1 frame of extra latency. |

Switch between them at any time; the change takes effect on the next captured frame.

### Log Level

Controls how much GamePeek writes to the Unity Console:

| Level | Output |
| --- | --- |
| **None** | Completely silent |
| **Error** | Errors only |
| **Warning** | Errors and warnings |
| **All** | Full diagnostic output |

### Adaptive quality

While streaming, the phone reports its delivery statistics once per second. If frames are getting lost or arriving slowly, GamePeek automatically spreads its packet bursts, then lowers JPEG quality, then the frame rate, and restores them (frame rate first) once the network is clean again. Every adjustment is logged to the Console at the **All** log level. The app's quality/fps settings always act as the ceiling.

---

## Touch Input

GamePeek injects touch into **every active input backend** simultaneously:

| Backend | How it works | Reliability |
| --- | --- | --- |
| **New Input System** (`com.unity.inputsystem`) | Virtual `Touchscreen` device via `InputSystem.QueueStateEvent` | Fully supported |
| **Legacy Input Manager** | Internal `Input.SimulateTouch(Touch)` via reflection | Best effort; works on Unity 2021, 2022, and 6, and may break on future versions |

The active backend is controlled by **Edit → Project Settings → Player → Active Input Handling**:

- **Input System Package (New):** only the new Input System path runs.
- **Input Manager (Old):** only the Legacy reflection path runs.
- **Both:** GamePeek injects into both systems at the same time. This is the recommended setting when your project uses a mix of old and new Input APIs.

GamePeek delivers touches through two additional channels regardless of backend:

| Channel | Class / API |
| --- | --- |
| **GamePeekInput events** | `GamePeekInput.OnTouch` / `OnTouchDetailed` |
| **Unity Input System polling** | `ETouch.activeTouches`, `Touchscreen.current`, etc. (requires new Input System) |

Touch overlays (semi-transparent circles) are drawn on the Game View automatically while touches are active.

> **Multi-touch, gyroscope, and accelerometer** are **Pro** features. Free tier receives single touch only.

### GamePeekInput

`GamePeekInput` is a lightweight static event bus. GamePeek fires its events on the main thread for every touch that arrives from the phone.

```csharp
using GamePeek;
using UnityEngine;

public class Example : MonoBehaviour
{
    void OnEnable()  => GamePeekInput.OnTouch += HandleTouch;
    void OnDisable() => GamePeekInput.OnTouch -= HandleTouch;

    void HandleTouch(Vector2 normalizedPos)
    {
        // normalizedPos.x : 0 = left edge,  1 = right edge
        // normalizedPos.y : 0 = top edge,   1 = bottom edge
        float screenX = normalizedPos.x * Screen.width;
        float screenY = (1f - normalizedPos.y) * Screen.height; // flip Y for Unity screen space
        Debug.Log($"Touch at screen ({screenX}, {screenY})");
    }
}
```

#### OnTouchDetailed

Use this when you need the finger ID or phase string:

```csharp
GamePeekInput.OnTouchDetailed += (fingerId, phase, normalizedPos) =>
{
    // phase: "began" | "moved" | "ended" | "canceled"
    Debug.Log($"Finger {fingerId} {phase} at {normalizedPos}");
};
```

#### When to use GamePeekInput

- You want a simple callback without dealing with the Input System device layer.
- You need to react to individual touch events (e.g. "tap began") rather than polling state every frame.

### New Input System integration

When `com.unity.inputsystem` is installed, GamePeek automatically injects all touches into a virtual `Touchscreen` device. You can read them using any standard Input System API.

#### Single touch: polling

```csharp
using UnityEngine.InputSystem;

var ts = Touchscreen.current;
if (ts != null && ts.primaryTouch.press.isPressed)
{
    Vector2 pos = ts.primaryTouch.position.ReadValue();
}
```

#### Multi-touch: Enhanced Touch

Enable `EnhancedTouchSupport` once (e.g. in `OnEnable`) then read `ETouch.activeTouches` every frame:

```csharp
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

void OnEnable()  => EnhancedTouchSupport.Enable();
void OnDisable() => EnhancedTouchSupport.Disable();

void Update()
{
    var touches = ETouch.activeTouches;

    // Filter out Ended/Canceled; they linger for one frame after lift
    int live = 0;
    foreach (var t in touches)
    {
        if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
            t.phase == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
        live++;
    }
}
```

> **Important:** Always filter out `Ended` and `Canceled` phases when counting active fingers.
> Enhanced Touch keeps a lifted touch in `activeTouches` for one frame after it ends,
> which can cause spurious multi-touch events if you only check `Count`.

#### Coordinate system

GamePeek's phone sends **normalised** coordinates where `(0, 0)` is the **top-left** of the screen. Unity's Input System uses **screen pixels** where `(0, 0)` is the **bottom-left**.

GamePeek applies the conversion automatically before injecting into the Input System:

```text
screenX = normalizedX × Screen.width
screenY = (1 − normalizedY) × Screen.height   // Y-flip
```

When reading via `GamePeekInput`, the coordinates are still in the raw normalised form (Y = 0 at top). Apply the same flip yourself when converting to screen or world space.

### Pinch-to-scale example

The finger-pair baseline must be reset whenever the tracked pair changes (e.g. a third finger joins, or one of the original two lifts while the other stays down).

```csharp
private float _prevPinchDist = -1f;
private int   _pinchId0 = -1, _pinchId1 = -1;

void Update()
{
    var touches = ETouch.activeTouches;

    ETouch t0 = default, t1 = default;
    int live = 0;
    for (int i = 0; i < touches.Count && live < 2; i++)
    {
        var ph = touches[i].phase;
        if (ph == UnityEngine.InputSystem.TouchPhase.Ended ||
            ph == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
        if (live == 0) t0 = touches[i]; else t1 = touches[i];
        live++;
    }

    if (live >= 2)
    {
        // Reset when the finger pair changes
        if (t0.touchId != _pinchId0 || t1.touchId != _pinchId1)
        {
            _prevPinchDist = -1f;
            _pinchId0 = t0.touchId;
            _pinchId1 = t1.touchId;
        }

        float dist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
        if (_prevPinchDist > 0f)
        {
            float ratio = dist / _prevPinchDist;
            float s = Mathf.Clamp(transform.localScale.x * ratio, 0.1f, 5f);
            transform.localScale = Vector3.one * s;
        }
        _prevPinchDist = dist;
    }
    else
    {
        _prevPinchDist = -1f;
        _pinchId0 = _pinchId1 = -1;
    }
}
```

### Touch Input quick reference

| What you want | Recommended API | Tier |
| --- | --- | --- |
| Simple tap callback | `GamePeekInput.OnTouch` | Free + Pro |
| Phase + finger ID | `GamePeekInput.OnTouchDetailed` | Free + Pro |
| Single-touch polling | `Touchscreen.current.primaryTouch` | Free + Pro |
| Multi-touch / pinch | `ETouch.activeTouches` (Enhanced Touch) | Pro |

---

## Sensor Input (Gyroscope / Accelerometer)

The phone streams gyroscope (rotation rate) and accelerometer (gravity + motion) samples to the editor while connected. Toggle **Gyroscope** and **Accelerometer** on in the app's settings panel (Input section; both are off by default).

> Sensor streaming is a **Pro** feature of the GamePeek app.

### Units

| Sensor | Value | Units / convention |
| --- | --- | --- |
| Gyroscope | Rotation rate around device x/y/z | rad/s, same as `Input.gyro.rotationRate` |
| Accelerometer | Acceleration incl. gravity | g-multiples, Unity convention: flat on a table, screen up ⇒ `(0, 0, -1)` |

The phone's raw accelerometer readings (m/s², Android sign convention) are converted automatically before injection.

### New Input System: works out of the box

When `com.unity.inputsystem` is installed, GamePeek injects sensor data into virtual `Gyroscope` and `Accelerometer` devices. They are enabled automatically; you do not need to call `InputSystem.EnableDevice`. Read them like real device sensors:

```csharp
using UnityEngine.InputSystem;
using Gyroscope = UnityEngine.InputSystem.Gyroscope; // disambiguate from UnityEngine.Gyroscope

void Update()
{
    if (Gyroscope.current != null)
    {
        Vector3 rotationRate = Gyroscope.current.angularVelocity.ReadValue(); // rad/s
    }

    if (Accelerometer.current != null)
    {
        Vector3 accel = Accelerometer.current.acceleration.ReadValue(); // g-multiples
    }
}
```

### Legacy Input Manager: use GamePeekInput events

Unity has no simulation hook for legacy sensors (`Input.SimulateTouch` has no gyro/accel equivalent), so `Input.gyro` and `Input.acceleration` always read **zero** in the editor. Subscribe to the `GamePeekInput` events instead; they fire on the main thread with **both** backends:

```csharp
using GamePeek;
using UnityEngine;

public class SensorExample : MonoBehaviour
{
    Vector3 _rotationRate; // rad/s, latest gyro sample

    void OnEnable()
    {
        GamePeekInput.OnGyro  += HandleGyro;
        GamePeekInput.OnAccel += HandleAccel;
    }

    void OnDisable()
    {
        GamePeekInput.OnGyro  -= HandleGyro;
        GamePeekInput.OnAccel -= HandleAccel;
    }

    void HandleGyro(Vector3 rotationRate) => _rotationRate = rotationRate;

    void HandleAccel(Vector3 acceleration)
    {
        // g-multiples, Unity convention: flat on a table, screen up ⇒ (0, 0, -1).
        // Drop-in replacement for Input.acceleration.
    }

    void Update() => transform.Rotate(_rotationRate * Mathf.Rad2Deg * Time.deltaTime);
}
```

### Sensor input quick reference

| What you want | Recommended API | Backend |
| --- | --- | --- |
| Rotation rate (rad/s) | `Gyroscope.current.angularVelocity` | New Input System |
| Acceleration (g) | `Accelerometer.current.acceleration` | New Input System |
| Either, as a callback | `GamePeekInput.OnGyro` / `OnAccel` | Any (incl. legacy-only) |

---

## Windows Firewall

On first launch, GamePeek asks for a one-time UAC (administrator) prompt to add Windows Firewall rules allowing inbound TCP and UDP connections on ports **7777-7786** (control channel + video). Without these, the phone cannot reach the editor, and without the UDP rule, video silently falls back to the slower TCP path.

If you declined the prompt or the rule was removed, click **Reset FW** in the toolbar and then **Start Streaming** again to re-run the setup.

If you prefer to add the rule manually, run this in an elevated PowerShell:

```powershell
New-NetFirewallRule -DisplayName "GamePeek" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7777-7786 -Profile Any
New-NetFirewallRule -DisplayName "GamePeek" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 7777-7786 -Profile Any
```

---

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| QR code shows `127.0.0.1` | Your machine has no active Wi-Fi or Ethernet. Connect to the network first. |
| Phone can't find the host via Browse | Both must be on the same subnet. Some guest/corporate Wi-Fi blocks device-to-device traffic; try the QR code or connect by IP instead. |
| Firewall prompt never appeared / connections fail | Click **Reset FW** in the toolbar, then **Start Streaming** again. |
| Stream is black or frozen | Make sure there is at least one camera tagged `MainCamera` in your scene. In Edit Mode, `Camera.main` must exist. |
| Touch events are not registering | Check **Edit → Project Settings → Player → Active Input Handling**. If using Legacy only, GamePeek injects via reflection (best-effort). For reliable injection, install `com.unity.inputsystem` and set Active Input Handling to **Input System Package** or **Both**. |
| Touch works but UI / Canvas buttons don't respond | Your Canvas is still using the old **Standalone Input Module**. Select the **EventSystem** in your scene and replace the Standalone Input Module component with **Input System UI Input Module** (`UnityEngine.InputSystem.UI.InputSystemUIInputModule`). |
| High latency or choppy video | Lower the quality setting in the app, switch to **Async GPU Readback**, or reduce resolution to 540p. |
| Stream drops on recompile | Disable **Run in Play Mode** to allow the stream to persist across domain reloads. |
| Console says "falling back to video-over-TCP" | The network blocks peer-to-peer UDP. On Windows click **Reset FW** to reinstall the firewall rules (TCP + UDP). On managed Wi-Fi the fallback keeps working, just with higher latency. |

---

## Default Ports

| Port | Purpose |
| --- | --- |
| **7777** (TCP) | Control channel: handshake, touch, configuration (configurable; retries 7778-7786 when taken) |
| **7777** (UDP) | Video + sensor datagrams (binds the same number as the TCP port) |
