
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

## Controls

| Key | Action |
|---|---|
| F9 | Toggle the creator camera |
| F10 | Cycle FOV presets |
| F11 | Switch between camera perspectives |
| F12 | Freeze or reattach the camera |
| Ctrl + F9 | Toggle local avatar visibility |
| F5–F7 | Original VR mirror modes |
| F8 | Camera diagnostics |
| F4 | Avatar diagnostics |
| Shift + F4 | Extended avatar diagnostics |

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

- Physical VR camera
- Additional creator and recording tools
- Improved camera controls
- More customisation options

## Disclaimer

This is an unofficial community mod and is not affiliated with the original OrbusVR developers.
