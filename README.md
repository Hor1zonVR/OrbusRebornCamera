
# OrbusRebornCamera

A creator and recording camera mod for **OrbusVR Reborn Community Edition**, built using BepInEx.

Capture OrbusVR from different perspectives without affecting your normal VR headset view.

## One-click GitHub publishing to RebornManager

RebornCam now has a [Publish RebornCam Update](https://github.com/Hor1zonVR/OrbusRebornCamera/actions/workflows/publish-camera.yml) GitHub Actions workflow. With the one-time private reference setup complete, commit changes to main, choose **Actions → Publish RebornCam Update → Run workflow → main**, and select Preview or Stable.

- **Preview** (default) publishes a test pre-release. RM ignores prereleases.
- **Stable** publishes a public GitHub Release with exactly one binary asset, `BetterMirror.dll`. RM already tracks this repository and can offer the new update when Orbus is closed.

The action tests keyframe code, builds against actual BepInEx/Unity IL2CPP reference DLLs, and blocks publishing on compilation failure or an existing version tag. The version comes from `Plugin.cs`; bump it before each subsequent release.

### One-time CI dependency setup

GitHub's hosted runner does not have the Orbus-generated BepInEx/IL2CPP references. Provide them **once**, privately, before running the publishing workflow:

1. Install GitHub CLI (gh) on the development PC and sign in using `gh auth login`. The modded test game must already contain generated interop DLLs.
2. Open PowerShell and run the setup helper:

```powershell
$script = Join-Path $env:TEMP "setup_ci_refs.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/Hor1zonVR/OrbusRebornCamera/main/tools/setup_ci_refs.ps1" -OutFile $script
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
```

3. The helper uploads an archive of **only the necessary reference DLLs** to a separate **private** repository named `Hor1zonVR/RebornCamBuildRefs`, under tag `refs-v1`. It refuses to upload to a public repo. Make sure any use/sharing of these generated libraries is allowed under applicable game/tool licenses.
4. Create a **fine-grained GitHub personal access token** scoped only to `Hor1zonVR/RebornCamBuildRefs` with **Contents: Read-only**, then add it to the public camera repo under **Settings → Secrets and variables → Actions** as secret `REBORNCAM_BUILD_REFS_TOKEN`. The helper creates the repository variable `REBORNCAM_BUILD_REFS_REPO` automatically.

Never commit those generated references to the public repository or use a broadly scoped token. After this one-time setup, future GitHub publishing needs only the Run workflow button.

**Status:** v0.9.1 source is on main, but no v0.9.1 DLL has been published yet. This is an experimental desktop **diagnostic**, not functional mouse-based VR menus, virtual hands, or headset-free server joining. Test using a Preview release before publishing Stable to RM users.

## Experimental Desktop Startup Probe v0.1 (merged into main)

This **does not yet make Orbus a flatscreen game**. It is a read-only feasibility probe to determine whether Community Edition loads without an active Quest/SteamVR headset, which scene and cameras initialize, and whether Unity physics raycasts from the desktop mouse hit world objects. It does not emulate VR controllers, click Orbus menus, teleport a character, or join a server.

Source branch: `feature/desktop-startup-probe-v0.1`. Install only in an isolated modded test instance. Open PowerShell on Windows and run:

```powershell
$script = Join-Path $env:TEMP "install_desktop_probe.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/Hor1zonVR/OrbusRebornCamera/feature/desktop-startup-probe-v0.1/tools/install_desktop_probe.ps1" -OutFile $script
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
```

The helper clones the branch into a new temporary directory, builds against your **own** game-generated BepInEx IL2CPP interop DLLs, and replaces the mod DLL **only after** a successful build. It creates a timestamped backup of any existing installed `BetterMirror.dll`. The new probe runs alongside the unchanged v0.9.0 creator camera.

**Testing checklist:**

1. Close Orbus and disconnect Quest Link/Air Link, then start the **modded** Orbus client normally. Do not bypass server authentication or other game restrictions.
2. Note whether the process reaches the main menu or world. If yes, press **F1** and inspect the probe overlay for scene, XR state and available cameras.
3. Move the mouse over the main menu / world. Left click to log the target of a physics raycast. A miss does **not** mean the interface is impossible to click with future virtual controller code: some UI uses a GraphicRaycaster without colliders.
4. If the game launches, send the lines starting with `[DesktopProbe]` from `BepInEx/LogOutput.log`. If it fails before BepInEx starts, send the Unity Player.log and the startup error message instead. Avoid sharing sensitive account or authentication details from logs.
5. If needed, revert the installed DLL by restoring the timestamped `.bak-desktopprobe-...` file to `BetterMirror.dll`, with Orbus closed.

The probe never injects UI button presses; the next development step is to inspect the actual Orbus menu interaction implementation before designing mouse-controlled laser input.

## Features

- Independent desktop camera for recording and streaming
- Adjustable horizontal field of view (50°–150°)
- First-person, third-person and selfie camera modes
- Static camera mode for recording from a fixed position
- Local avatar rendering in external camera modes
- Configurable camera distance and smoothing
- Optional avatar visibility toggle
- Separate desktop and headset camera rendering
- **Universal freecam** for both VR-launched and headset-free game sessions (when Orbus itself can run without XR), with keyboard/mouse flight
- In-game **F2 Creator Panel** for POV / third-person / selfie / static / freecam, FOV, smoothing, speed and local-avatar controls
- **Cinematic keyframes**: add shots, undo, play or stop smooth paths, choose duration, automatically save/load paths

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
| Ctrl + F11 | Enter/exit freecam (same mode in VR and non-VR sessions) |
| Hold right mouse button + WASD | Look around and fly in freecam |
| Right mouse + Q/E | Descend/ascend |
| Right mouse + Shift/Ctrl | Move faster/slower |
| F12 in freecam | Freeze the camera at its current position |
| Shift + F4 | Extended avatar diagnostics |
| Ctrl + F4 | Inspect loaded Trickster Goblin candidate models (only when requested) |

## Desktop Creator Panel (v0.9.0)

Press **F2** while the OrbusVR desktop window is focused to show or hide the Creator Panel. The panel is hidden by default, and can optionally be displayed at launch with `[CreatorPanel] OpenAtStartup = true` in the BepInEx config.

The desktop panel lets you:

- Enable/disable the independent recording camera
- Switch directly to **First person**, **Third person**, **Selfie** or **Static / detached** without cycling hotkeys
- Adjust horizontal FOV (50–150°), follow smoothness (0–20), and third-person/selfie distance (0.5–8 metres)
- Toggle local avatar rendering in external desktop views
- Detach/freeze the camera where it is and reattach to the previous follow mode
- Enter freecam and adjust flight speed
- Record multiple cinematic shots and play saved camera paths with position, rotation and FOV interpolation

Changes to camera settings are written to the existing BepInEx configuration entries and persist across launches. Existing F9/F10/F11/F12 keyboard shortcuts continue to work. Freecam is now available directly from the panel or Ctrl+F11 when the server owner permits it.

**Recording note:** This first prototype uses Unity's in-game desktop IMGUI overlay, so the panel **may appear in footage** captured from the desktop game window. Press F2 to hide it before taking a clean recording. A separate creator-control window that is reliably excluded from capture is a possible later feature.

**IL2CPP dependency:** The game must have generated `BepInEx/interop/UnityEngine.IMGUIModule.dll` for this version to build; the project explicitly checks for it. The build must use the same local game/BepInEx interop folder as the running mod.

## Automatic Trickster diagnostic in RebornCam v0.8.2 — no Python

RebornCam can now check your **installed local game files while you're playing**. This runs read-only on a background thread, starts about 25 seconds after launching the game, and writes `BepInEx/TricksterAssetReport.txt`. It does not need a live goblin spawn or a hotkey. It only scans once per installation; delete the existing report to scan again.

Since freecam has owner approval, the v0.9.0 default is `[Diagnostics] AutoScanTricksterFiles = false` to avoid background disk I/O while playing. Set it to `true` only if you want the optional offline string scan. **Ctrl+F4 works immediately without changing any config** when you come across the rare Trickster Goblin.

**Quick source update + local build/install (Windows):** Open PowerShell inside the existing `OrbusRebornCamera-1` project directory, then run:

```powershell
$helper = Join-Path $env:TEMP "update_trickster_diagnostic.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/Hor1zonVR/OrbusRebornCamera/main/tools/update_trickster_diagnostic.ps1" -OutFile $helper
& $helper
```

This downloads only the three C# source files needed, compiles against your installed BepInEx interop assemblies with `dotnet build`, backs up the existing mod DLL as `.bak`, then copies the new DLL to `BepInEx/plugins`. It prompts for the modded Orbus game folder if the default test path is unavailable. The previous DLL is not overwritten if the build fails. Close Orbus before running the updater.

**Scan limits:** It looks for raw ASCII/UTF-16LE identifiers such as `TreasureGoblin` and `goblinstore`. Encrypted or compressed Unity bundles may not expose these strings; a negative result doesn't prove the prefab is absent. This diagnostic does not filter the goblin; freecam is a separate, owner-permitted creator feature.

## Offline Trickster asset scan (no rare spawn required)

For the first identification pass, scan the **extracted PC client files** without launching Orbus. The read-only Python script [`tools/scan_trickster_assets.py`](tools/scan_trickster_assets.py) searches asset files and IL2CPP binaries for `treasuregoblin`, `TreasureGoblin`, `trickster` and related terms. It doesn't modify the installation.

On Windows with Python 3.11 installed, paste into PowerShell:

```powershell
$scanner = Join-Path $env:USERPROFILE "Downloads\scan_trickster_assets.py"
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/Hor1zonVR/OrbusRebornCamera/main/tools/scan_trickster_assets.py" -OutFile $scanner
py -3 -m pip install UnityPy
py -3 $scanner --deep
```

Choose the **extracted game folder** in the folder picker (`New-Client` or its parent for Renewed; `vrclient.exe`'s folder for Reborn). The result is `Downloads\trickster_asset_report.json`. Send only that small report for investigation, not the game files.

`UnityPy` is optional: omit `--deep` and the installation command for a dependency-free raw string scan. The `--deep` option is preferred because it also parses Unity object names inside readable compressed bundles. Depending on the game build, some serialized asset files may not be parsable; any errors appear in the report. No match is **not** proof that the model is absent or that a public freecam filter is ready.

## Freecam and cinematic paths

RebornCam v0.9.0 uses **one independent desktop recording camera**, no matter whether Orbus was launched with an active headset or without one. It never changes the player position or the VR headset/controller transforms.

- Press **F2** and choose **Enter FREECAM** (or **Ctrl+F11**).
- Hold **right mouse** to steer using **WASD**; **Q/E** go down/up, **Shift** boosts, **Ctrl** slows. Releasing right mouse restores the cursor.
- The F2 panel adjusts movement speed. **F12** freezes the recording view; press F12 again to return to the follow view.
- Position the camera, click **Add camera shot**, move to the next shot, and repeat. With at least two shots, select **Play camera path**. The slider controls total path duration (2–120 seconds).
- Shots record world position, rotation and horizontal FOV, and persist to `BepInEx/config/RebornCamKeyframes.txt`. Paths are tied to their original Unity scene; other scenes cannot play them. **Undo** removes the last shot, and **Clear** requests confirmation.
- The F2 panel shares these exact controls in both VR and non-VR sessions.

**Headset-free caveat:** The mod no longer blocks camera startup based only on XR status, and it can use an available non-XR game camera as a fallback. Orbus itself still needs to launch, connect and create a world camera. A genuinely headset-free game startup has not been validated and is not guaranteed by this mod.

**Trickster Goblin:** The rare `treasuregoblin` enemy is distinct from the shop NPC `goblinstore`. The model is **not yet hidden** from freecam, and its Unity identity has not been confirmed. The user has reported explicit approval from their server owner to use freecam while we work on the diagnostic. Do not assume this grants permission on other servers.

If you encounter the rare Trickster Goblin, press **Ctrl+F4** once. The read-only inspector logs candidate GameObject names, parent hierarchies and model mesh/material names in `BepInEx/LogOutput.log` under `[TricksterAssets]`. It does not move the character, spawn creatures or collect live coordinates. If the model has a generic Unity name, the inspector may still need further refinement. An optional raw-file scanner can also write `BepInEx/TricksterAssetReport.txt`; it is disabled by default to avoid disk I/O during ordinary play.

### Update from source without Python

Open PowerShell inside your `OrbusRebornCamera-1` source folder (with the .NET SDK and generated BepInEx interop files available), then run:

```powershell
$helper = Join-Path $env:TEMP "update_freecam.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/Hor1zonVR/OrbusRebornCamera/main/tools/update_freecam.ps1" -OutFile $helper
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper
```

Select the correct modded Reborn or Renewed instance when prompted. The helper updates the changed C# files, builds against the selected instance's generated interop assemblies, backs up the installed `BetterMirror.dll` and installs the new one **only if compilation succeeds**. If anything breaks in-game, close Orbus and restore the old DLL from `BetterMirror.dll.bak`.

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

Designed for **OrbusVR Reborn Community Edition** and potentially compatible Renewed IL2CPP clients; build/runtime compatibility depends on each client's BepInEx interop files.

OrbusVR Classic and Preborn are not supported.

## Future Plans

- Identify Trickster Goblin mesh and hide it from every freecam camera shot without altering normal gameplay
- Polish the desktop Creator Panel, including a separate capture-safe window option
- Verified headset-free game boot, if supported by Community Edition
- Expand cinematic paths with a visual timeline, editable shots and multiple named paths
- Physical VR camera (optional; currently an experimental placeholder)
- Additional creator and recording tools
- Improved camera controls
- More customisation options

## Disclaimer

This is an unofficial community mod and is not affiliated with the original OrbusVR developers.
