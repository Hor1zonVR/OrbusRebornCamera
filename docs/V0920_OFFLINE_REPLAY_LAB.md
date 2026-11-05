# RebornCamera v0.9.20 — Offline Replay Lab

## Purpose

This is the first **server-independent pose-preview mode**. From the Orbus
main menu (assuming the client starts normally and RC's F2 panel is available),
you can load an existing RC pose clip and watch the saved head/hand movement
without joining the server where it was recorded.

**This version does NOT load Highsteppe, dungeons, map terrain or a recorded
instance offline.** Instead the camera deliberately has a solid dark
background, 20x20-metre guide grid, recorded head/hand crosshairs and arm
connections. This is a diagnostic foundation for future static world loading.

## What's new

- F2 Studio has five tabs: CAM / FREE / SHOTS / REPLAY / OFFLINE.
- OFFLINE tab: Load latest clip (no live camera required), Start/End Offline
  Preview, Focus recorded head and Export Read-Only World Inventory.
- Offline preview creates/configures a *separate desktop-only RC camera* with
  culling mask 0. It does **not** touch Orbus's eye/main camera, local avatar,
  tracking, gameplay physics, shard or network. It draws local clip poses
  on a generated grid using the existing desktop GL marker renderer.
- The existing REPLAY tab controls Play, Pause and timeline scrubbing in
  both regular in-world preview and offline preview.
- The offline camera skips normal live-game FOV/culling synchronization and
  player-centred streaming/recentring. Normal RMB+WASD/Q/E fly controls still
  work; the recorded positions stay in clip world coordinates.
- Ending preview, disabling RC or unloading the component switches off the
  dedicated camera and stops the offline overlay. Press F9 to re-enable the
  normal desktop camera when back inside a server.
- A **read-only, bounded asset inventory** enumerates plausible Unity
  assets and installed StreamingAssets filenames under the local game Data
  folder. It also logs the currently loaded Unity scene names. It does not
  read asset contents, invoke a game world loader or make network requests.

## How to test from the main menu

1. Install v0.9.20 after publishing BetterMirror.dll through your existing
   local `release.ps1` workflow (source is in GitHub main).
2. Ensure a saved `RC_Clip_*.tsv` exists in the **selected RM instance's**
   `BepInEx/config/OrbusRebornCamera/Clips/` folder.
3. Launch Orbus and remain at the main menu (no server join).
4. Press F2, open OFFLINE, and click **START OFFLINE PREVIEW**. The latest
   saved clip should load automatically; if none is found, Save Clip or copy
   your old recording to the Clips folder.
5. You should see **only the local dark grid and recorded markers**, no
   real Highsteppe scenery. Go to REPLAY to Play/Pause/seek.
6. Reopen OFFLINE and choose **END OFFLINE PREVIEW** to restore the main
   menu view. If already in a world, press F9 to re-enable the ordinary RC
   creator camera.
7. In OFFLINE, choose **EXPORT READ-ONLY WORLD INVENTORY** and inspect the
   resulting `RC_OfflineAssets_*.txt` under:
   `BepInEx/config/OrbusRebornCamera/OfflineReports/`
8. Send the report and relevant `[OfflineReplay]` lines in the BepInEx log
   if you want help identifying how the game packages zones.

**Before sharing an inventory report publicly:** it includes local install
paths. You may redact those paths if desired. The mod does not transmit it.

## Technical direction for real offline world loading

The next research step is to map scene names (e.g. ZoneHighsteppe) to
installed resource bundles, scene dependencies, asset references, and the
runtime initialization pathway. Only after this is established should we
try an opt-in static scene load from local assets. Live player/entity state
will still require recording and reconstruction, not server streaming.

## Limitations and safeguards

- Replay pose files are still v1/v2 TSV; no format migration is required.
- No game object/character/zone reconstruction; only a positional preview.
- Some game installations may not have a usable F2 panel at the main menu,
  or may require XR initialization to launch. Report this rather than assuming
  server-less mode works on every install.
- The local inventory is capped at 240 directories, 1,800 scanned files,
  320 listed likely assets and traversal depth 4. On larger installations the
  report can be partial.
- The offline stage is not a standalone app and does not circumvent game
  launch/authentication or use a private server endpoint.
- World-content availability cannot be deduced from filenames alone.

## PowerShell release, regardless of current terminal directory

```powershell
$rc = @(
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1",
  "$env:USERPROFILE\Documents\OrbusRebornCamera"
) | Where-Object { Test-Path (Join-Path $_ "release.ps1") } | Select-Object -First 1

if (-not $rc) { throw "RC repository was not found in the usual locations." }
& (Join-Path $rc "release.ps1")
```

GitHub Actions' public integration tests don't use real IL2CPP game binaries;
the automatic release publisher still needs private build-reference setup.
The local release script compiles with the actual installed BepInEx interop
and publishes the successful BetterMirror.dll to the existing GitHub release
flow for RM.
