# RebornCamera v0.9.19: first local pose replay viewer

## What was added

- Existing five-minute rolling headset/controller buffer is retained.
- Saved clip TSV **v2** adds two columns at the right: `zone` and `transition`.
  Zone is the set of loaded non-bootstrap Unity scene names; it is a best-effort
  local diagnostic, **not a verified Orbus server shard or instance ID**.
- The recorder classifies a large sudden head relocation as **Teleport** when
  distance >12m AND speed >35m/s, a gap >0.25s as **Gap**, and a change in
  known loaded scenes as **ZoneChange**. Some unusual high-speed abilities
  may be mislabeled; this is a prototype threshold, not official telemetry.
- The pose clip reader loads **both v1 and v2** TSV; old v1 clips detect their
  transitions from saved timestamps and head position.
- A **desktop-only pose viewer** is integrated into F2 -> RECORDING:
  * **Load latest saved clip** (local files only)
  * **Focus recorded head** (moves the detached desktop camera)
  * **Play / Pause** and **Close replay**
  * **Replay time** slider (scrub / seek)
  * Head, left-hand and right-hand crosshair markers; their forward-axis lines
    show orientation. Head is blue, left hand orange, right hand green.
- Normal movement/rotation interpolates smoothly. During teleports, gaps and
  loaded-zone transitions, playback holds the earlier position until the next
  real recorded sample and snaps; it never invents a path through missing data.
- Markers are drawn with the existing GL helper **only through the separate RC
  desktop creator camera**. No avatar, live player, VR tracking, game physics
  colliders or server objects are changed.
- Markers remain visible when F2 is hidden; they disappear if the replay is
  closed or RC freecam is exited.

## Usage / first test

1. Publish and install the new DLL using the existing local `release.ps1`
   workflow. Automatic GitHub Actions release requires missing private build
   references and will fail until those are configured.
2. Place a saved clip (e.g. the user's original v0.9.18 TSV) into the **active
   RM Orbus game instance**:
   `BepInEx/config/OrbusRebornCamera/Clips/`
   Save Clip places new clips in this folder automatically.
3. Launch Orbus in a loaded world, then press **F2 -> RECORDING**.
4. Press **Load latest saved clip**. It enters existing RC freecam and frames
   the recorded head. Only current loaded scene geometry is available.
5. Press **Play replay**, pause, or drag the time slider to scrub. The crosshair
   markers represent recorded head/hand poses, NOT actual avatars. Press
   **Focus recorded head** after seeking to snap your freecam near that pose.
6. Hold **RMB+WASD** outside the creator panel to fly around; release to scrub.
   F2 hides the editor for desktop capture; reopening F2 restores controls.
7. Press **Close replay** to stop displaying markers; live VR play is unaffected.

## Important limits

- This is a **pose viewer**, not the full replay system. Other players, NPCs,
  weapons, projectiles, animations, spell effects, physics state and world state
  are NOT recorded or recreated. Party Sync is not implemented.
- Saved world positions refer to the original recording's coordinate space.
  If that zone is currently unloaded, the markers can be in empty space. The
  viewer does not load missing scenery, switch your server or teleport avatar.
- No player location/hidden entity scanning and no synthetic network calls.
- The loader accepts local RC_Clip_*.tsv files in the configured Clips folder
  only; it limits file size to 25MB and samples to 30,000.
- Playback is 1x for this first implementation. Clip storage and replay
  editor can be improved after confirming correct first playback in Orbus.
- User's original sample was measured to have **8,940 pose frames**, one
  ~212.4m sudden jump and gaps of roughly 0.909s and 0.409s. Those user
  coordinates and the actual original clip are **not committed** to GitHub.

## VS Code / Visual Studio PowerShell release (from any current directory)

```powershell
$rcCandidates = @(
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1",
  "$env:USERPROFILE\Documents\OrbusRebornCamera"
)
$rc = $rcCandidates | Where-Object {
  Test-Path (Join-Path $_ "release.ps1")
} | Select-Object -First 1
if (-not $rc) { throw "RebornCamera clone not found." }
& (Join-Path $rc "release.ps1")
```

The script pulls GitHub main and compiles against local BepInEx-generated
IL2CPP interop. If it fails, paste the compiler error and BepInEx log.
Unit/compile-stub CI tests are not a substitute for the real DLL build.
