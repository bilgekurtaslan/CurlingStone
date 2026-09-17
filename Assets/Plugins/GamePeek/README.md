# GamePeek Unity Editor Plugin

Stream the Unity Game View to the **GamePeek app** on your iOS or Android device in real-time over a local Wi-Fi network.

---

## Features

| Feature | Free | Pro (via app) |
| --- | --- | --- |
| Low-latency UDP video streaming (auto TCP fallback) | ✅ | ✅ |
| Network-adaptive quality (pacing → quality → fps) | ✅ | ✅ |
| Remote play via Tailscale | ✅ | ✅ |
| mDNS / DNS-SD auto-discovery | ✅ | ✅ |
| QR code pairing | ✅ | ✅ |
| Single touch input | ✅ | ✅ |
| Touch gizmo overlay (Game View circles) | ✅ | ✅ |
| Multi-touch injection | 10 min/day | ✅ |
| Gyroscope / accelerometer injection | 10 min/day | ✅ |
| 540p + 720p streaming | ✅ | ✅ |
| 1080p streaming | 10 min/day | ✅ |
| Up to 120 fps | 10 min/day | ✅ |
| Multiple connected devices | ❌ | ✅ |

> **Note:** The plugin itself never enforces tier limits; the companion app controls them. Since app v2.5, Free includes 10 minutes of full Pro quality per day. When the allowance is spent, streaming pauses until the next day.

---

## Unity Version Requirements

| Unity | Status |
| --- | --- |
| 2021 LTS (2021.3.x) | ✅ Supported |
| 2022 LTS (2022.3.x) | ✅ Supported |
| Unity 6 (6000.x) | ✅ Supported |
| 2020 and earlier | ⚠️ Not tested |

Requires **.NET Standard 2.1** API Compatibility Level (`Edit → Project Settings → Player → Other Settings → Api Compatibility Level`).

**Input injection:**

- **Legacy Input Manager:** single touch works via internal reflection (`Input.SimulateTouch`). This is best effort; gyroscope and accelerometer are not available.
- **New Input System** (`com.unity.inputsystem`): full touch, multi-touch, gyroscope, and accelerometer injection via virtual devices. Recommended.
- **Both:** GamePeek injects into both backends simultaneously.

---

## Installation

### 1. Install the plugin

Import GamePeek from the Unity Asset Store (**Window → Package Manager → My Assets**).

### 2. Open the window

```text
Unity menu → Window → GamePeek
```

## Windows

> On first launch on Windows, GamePeek will prompt for a one-time UAC elevation to add Windows Firewall inbound rules for TCP and UDP ports 7777-7786 (control channel + video).

## macOS & Linux

> No additional permissions are required.

---

## Quick-start: Pairing via QR Code

1. Open the **GamePeek** window (`Window → GamePeek`).
2. Click **▶ Start Streaming**.
   A QR code appears showing the local IP and port.
3. Open the **GamePeek** companion app on your phone.
4. Tap **Scan QR** and point the camera at the QR code.
5. The connection indicator in the Editor turns green; the phone now shows the live Game View.

---

## Quick-start: Pairing via mDNS (no QR)

The plugin broadcasts `_unipeek._tcp` on the local network using mDNS / DNS-SD (RFC 6762).
The companion app discovers the host by itself; tap the machine name when it appears.

Both the Unity host and the phone must be on the **same Wi-Fi network** (or the same network segment).

---

## Remote Play via Tailscale

GamePeek's official remote path is [Tailscale](https://tailscale.com), a zero-config WireGuard mesh. The protocol is tunnel-friendly by design (every UDP datagram fits the WireGuard MTU with headroom), so remote streaming behaves like LAN streaming, just with your internet latency added.

1. Install Tailscale on **both** the editor machine and the phone, signed into the same tailnet.
2. Click **▶ Start Streaming** in the GamePeek window.
3. On the phone, connect **by IP**, entering the editor machine's tailnet address (the `100.x.y.z` IP shown in Tailscale) and GamePeek's port.

Notes:

- The QR code and mDNS discovery **do not cross the tunnel**; they advertise the LAN address. Manual IP entry is the expected flow for remote play.
- On iOS, connecting to a tailnet IP does **not** require the Local Network permission (that permission only governs LAN discovery and LAN peer connections).
- If the tunnel blocks UDP for any reason, the automatic video-over-TCP fallback keeps the stream alive.

---

## Settings Reference

| Setting | Options | Description |
| --- | --- | --- |
| **Editor Name** | Text field | Display name shown in the app's device list. Defaults to the machine name. |
| **Run in Play Mode** | On / Off | **On:** streaming only runs while the Editor is in Play Mode. **Off:** streaming runs in both Edit and Play Mode (stream will briefly drop on domain reloads). |
| **Capture Method** | Camera Render / Async GPU Readback | Camera Render is synchronous. Async GPU Readback reduces main-thread stall at the cost of ~1 frame of extra latency. |
| **Log Level** | None / Error / Warning / All | Console verbosity for GamePeek diagnostic messages. |
| **Port** | Integer (default 7777) | TCP control port the editor listens on; the UDP video socket binds the same number. Only editable when not streaming. |

Settings are persisted in `EditorPrefs` and restored on next launch.

---

## Message Protocol

GamePeek v2 speaks a custom two-channel wire protocol; the full specification lives in [`docs~/PROTOCOL.md`](docs~/PROTOCOL.md):

- **TCP control channel** (port 7777, configurable): length-prefixed binary frames carrying the handshake (`HELLO`/`WELCOME`), configuration, touch input, play-mode state, RTT pings, and stats. JSON payloads, little-endian framing.
- **UDP video channel** (same port number): JPEG frames split into slices and chunks of at most 1100 bytes. The latest frame wins; there are no retransmits. Gyroscope and accelerometer readings ride the same socket as compact binary datagrams.
- **Automatic TCP fallback**: if the editor receives no UDP hole-punch within 2 seconds (some corporate/guest Wi-Fi blocks peer-to-peer UDP), video transparently falls back to the control channel. A later hole-punch switches it back; there is nothing to configure.

Touch `x`/`y` are normalised [0, 1]; `x=0` is the left edge, `y=0` is the **top** edge of the phone screen.
Gyro values are rad/s; accelerometer values arrive in m/s² and are converted to Unity's g-multiples convention before injection.

Canonical byte fixtures for protocol implementers live in `docs~/fixtures/`; `tools~/gamepeek_test_client.py` is a full reference client.

---

## Input Injection

### New Input System (required)

Input injection requires `com.unity.inputsystem`. GamePeek creates virtual devices and injects events via `InputSystem.QueueStateEvent`:

- `Touchscreen`: single and multi-touch from the phone (multi-touch requires Pro)
- `Accelerometer`: gravity + motion data (Pro)
- `AttitudeSensor`: gyroscope / rotation-rate data (Pro)

Ensure you have `com.unity.inputsystem` in your `Packages/manifest.json`.

### Legacy Input Manager

Single touch injection is supported via internal Unity reflection (`Input.SimulateTouch(Touch)`). This is best effort and may break on future Unity versions. Gyroscope and accelerometer injection are **not available** in Legacy mode; use the new Input System for those.

When **Active Input Handling** is set to **Both**, GamePeek injects into the Legacy Input Manager and the new Input System at the same time.

---

## Performance Notes

| Resolution | Expected FPS |
| --- | --- |
| 540p | >60 fps stable |
| 720p | 40-60 fps |
| 1080p | 30-40 fps |

- **Main-thread budget:** < 2 ms per frame (capture + one memcpy per slice only).
- **JPEG encoding** runs on up to 4 parallel background workers, one horizontal slice each; the network send runs on its own thread behind a latest-frame mailbox.
- **Static scenes send nothing.** Unchanged frames are detected per slice and skipped entirely.
- **Adaptive quality:** when the phone reports packet loss or slow delivery, GamePeek first spreads its packet bursts, then steps quality and fps down; it restores them once the network is clean.
- The capture loop drops frames automatically when the encoder is still busy (back-pressure).

---

## Troubleshooting

| Problem | Solution |
| --- | --- |
| QR code shows `127.0.0.1` | Machine has no active Wi-Fi / Ethernet. Connect to the network first. |
| Phone can't find host via mDNS | Make sure both are on the same subnet. Some corporate Wi-Fi isolates clients; try the QR code or connect by IP instead. |
| Firewall rule prompt never appears | Click **Reset FW** in the GamePeek toolbar, then Start Streaming again. |
| Game View is black / null capture | Open a **Game** tab in the Editor and make sure it is visible (not behind other panels). In Edit Mode, ensure a camera tagged `MainCamera` exists. |
| Touch events not registering | Check **Active Input Handling** in Player Settings. Legacy mode uses best-effort reflection. For guaranteed injection, install `com.unity.inputsystem` and set to **Input System Package** or **Both**. |
| High encode latency | Switch Capture Method to **Async GPU Readback**, lower Quality, or reduce Resolution. |
| Stream drops on recompile | Disable **Run in Play Mode** so the stream persists across domain reloads. |
| Video is laggy and the Console says "falling back to video-over-TCP" | The network blocks peer-to-peer UDP. On Windows, click **Reset FW** to reinstall the firewall rules (TCP + UDP); on managed Wi-Fi, the TCP fallback keeps working at a higher latency. |
| Stream stutters rhythmically (about twice a second) | AirDrop/Handoff briefly pulls the Wi-Fi radio away on Macs and iPhones. Turn AirDrop receiving off on the phone, and use Ethernet (or turn off AirDrop and Handoff) on the Mac. |

---

## License

GamePeek plugin: distributed exclusively via the Unity Asset Store under the [Asset Store EULA](https://unity.com/legal/as-terms).
QRCoder: **MIT** (<https://github.com/codebude/QRCoder>)
