
# OrbusRebornCamera

A creator and recording camera mod for **OrbusVR Reborn Community Edition**, built using BepInEx.

Capture OrbusVR from different perspectives without affecting your normal VR headset view.

## Features

- Independent desktop camera for recording and streaming
- Adjustable horizontal field of view (50°–150°)
- First-person, third-person and selfie camera modes
- Static camera mode for recording from a fixed position
- Local avatar rendering in external camera modes
- Configurable camera distance and smoothing
- Optional avatar visibility toggle
- Separate desktop and headset camera rendering
- **Experimental opt-in desktop drone camera** with mouse-look and keyboard flight (private tests only)
- In-game **desktop Creator Panel** for choosing POV / third-person / selfie / static, changing FOV, smoothing and distance, toggling local avatar and detaching the recording view

## Controls

| Key | Action |
|---|---|
| F2 | Show or hide the desktop Creator Panel |
| F9 | Toggle the creator camera |
| F10 | Cycle FOV presets |
| F11 | Switch between camera perspectives |
| F12 | Freeze or reattach the camera |
| Ctrl + F9 | Toggle local avatar visibility |
| F5–F7 | Original VR mirror modes |
| F8 | Camera diagnostics |
| F4 | Avatar diagnostics |
| Ctrl + F11 | Enter/exit experimental drone mode (only when explicitly enabled) |
| Hold right mouse button + WASD | Look around and fly in drone mode |
| Right mouse + Q/E | Descend/ascend |
| Right mouse + Shift/Ctrl | Move faster/slower |
| F12 in drone mode | Freeze the drone camera at its current position |
| Shift + F4 | Extended avatar diagnostics |

## Desktop Creator Panel (v0.8.1 prototype)

Press **F2** while the OrbusVR desktop window is focused to show or hide the Creator Panel. The panel is hidden by default, and can optionally be displayed at launch with `[CreatorPanel] OpenAtStartup = true` in the BepInEx config.

The desktop panel lets you:

- Enable/disable the independent recording camera
- Switch directly to **First person**, **Third person**, **Selfie** or **Static / detached** without cycling hotkeys
- Adjust horizontal FOV (50–150°), follow smoothness (0–20), and third-person/selfie distance (0.5–8 metres)
- Toggle local avatar rendering in external desktop views
- Detach/freeze the camera where it is and reattach to the previous follow mode

Changes to camera settings are written to the existing BepInEx configuration entries and persist across launches. Existing F9/F10/F11/F12 keyboard shortcuts continue to work. The experimental drone is **not unlocked** by the panel.

**Recording note:** This first prototype uses Unity's in-game desktop IMGUI overlay, so the panel **may appear in footage** captured from the desktop game window. Press F2 to hide it before taking a clean recording. A separate creator-control window that is reliably excluded from capture is a possible later feature.

**IL2CPP dependency:** The game must have generated `BepInEx/interop/UnityEngine.IMGUIModule.dll` for this version to build; the project explicitly checks for it. The build must use the same local game/BepInEx interop folder as the running mod.

## Experimental desktop drone — private testing

**Not suitable for public multiplayer yet.** Freecam could reveal the location of OrbusVR's randomly spawned collectible coin. The object's prefab/layer has not been identified, and collectible-specific camera filtering is **not implemented**. The drone therefore defaults to **disabled** and must not be treated as an anti-cheat-safe public feature. Do not enable it on public servers.

The v0.8.0 prototype operates the existing **desktop recording camera**, never the player avatar or tracked headset. To test it in a private environment:

1. Run OrbusVR Reborn once with the updated mod installed, then close the game.
2. Open `BepInEx/config/com.horizon.orbus.bettermirror.cfg`.
3. Under `[ExperimentalDrone]`, set `EnableExperimentalDrone = true`.
4. Start the game and **load into a private test world**. Focus the desktop game window and press **Ctrl+F11**.
5. Hold **right mouse** to control the drone. Use WASD to move, Q/E for up/down, Shift to boost, and Ctrl for slower movement. Release right mouse to stop movement and regain the pointer. F12 freezes the shot. Press Ctrl+F11 again to return to the last player-follow camera. F9 turns the creator camera off.

The camera has configurable movement speed, boost multiplier, mouse sensitivity and smoothing under `[ExperimentalDrone]`. Defaults are deliberately modest. Previous POV, third-person, selfie, static and F5–F11 shortcuts are preserved.

**Headset-free limitation:** This prototype removes the active-XR requirement **from the drone camera activation path only**. OrbusVR itself still has to launch, load a scene, and create its `Camera (eye)` camera. Whether the Community Edition game can do that with **no headset connected** is not yet verified. If it can't, the first practical test is with a connected headset that you don't have to wear, while driving the camera using mouse and keyboard. No non-VR character control or game-login replacement is included.

The drone temporarily locks the desktop mouse cursor only while right mouse is held. Leaving drone mode, closing the game window, or disabling the camera restores its previous cursor state. The prototype does not touch OrbusVR's VR camera transforms.

## Installation

**Requires:** OrbusVR Reborn Community Edition for Windows and BepInEx 6 Unity IL2CPP.

1. Download the DLL from the latest GitHub Release.
2. Place it inside your game's `BepInEx/plugins/` directory.
3. Launch OrbusVR Reborn.
4. Use the desktop camera hotkeys to control recording.

You can also install this mod through **OrbusRebornManager** once it is available in the approved mod catalogue.

## Building from source

This project currently uses locally generated BepInEx IL2CPP interop assemblies from a working OrbusVR installation.

Requirements:
- .NET 6 SDK or a newer compatible .NET SDK
- OrbusVR Reborn Community Edition
- BepInEx 6 Unity IL2CPP
- Generated BepInEx interop assemblies

From the repository root, build using:

```powershell
dotnet build .\OrbusRebornCamera\BetterMirror.csproj -c Release
```

The compiled DLL will appear at:

`OrbusRebornCamera/bin/Release/net6.0/BetterMirror.dll`

The original internal BetterMirror plugin identifier is preserved for compatibility with existing configurations.

## Compatibility

Designed for **OrbusVR Reborn Community Edition**.

OrbusVR Classic and Preborn are not supported.

## Future Plans

- Safe public-server drone release after randomly spawned collectibles can be excluded from footage
- Polish the desktop Creator Panel, including a separate capture-safe window option
- Verified headset-free game boot, if supported by Community Edition
- Full keyframe camera paths and cinematic recording controls
- Physical VR camera (optional; currently an experimental placeholder)
- Additional creator and recording tools
- Improved camera controls
- More customisation options

## Disclaimer

This is an unofficial community mod and is not affiliated with the original OrbusVR developers.
