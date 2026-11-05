# RebornCamera v0.9.21 — Local avatar recording prototype

This is the first step from tracking crosses toward recording and replaying
a character. It retains the proven v0.9.20 offline pose preview and 1/3/5
minute rolling buffers.

## What is actually recorded

- Existing local VR head, left controller and right controller world poses
  at the target 30 samples per second.
- **NEW:** a local avatar anchor pose (position and rotation) from a
  best-effort **Minicam** skinned-mesh renderer, when RC can identify one
  near the local eye. The renderer is cached; a bounded scan is attempted
  at most once per 12 seconds. A missing/ambiguous avatar means an invalid
  anchor flag, not a fabricated position.
- **NEW:** clip TSV **v3** appends columns for
  `avatar_valid avatar_x avatar_y avatar_z avatar_qx avatar_qy avatar_qz avatar_qw`.
- **NEW:** clip header includes `avatar_mesh_hint`,
  `avatar_bone_count_hint`, and explicit flags that mesh geometry and
  full animations are **not** being serialized.
- **NEW:** on Save Clip, a separate `RC_Clip_*.avatar.txt` descriptor
  captures the candidate's mesh name, bone names, material names and a
  vertex-count hint. This is a short **manifest**, not the model/mesh
  geometry, textures, skin weights, equipment, or animation clip.
  The descriptor is collected on the main thread, then written alongside
  the clip on the disk worker. Clips still save if the descriptor fails.
- The existing `Ctrl+Shift+C` save command and rolling history remain
  uninterrupted.

## Playback

- F2 -> **REPLAY** -> **Load latest saved clip**.
- A thin, recognisable humanoid guide is now drawn along with the existing
  head/hand debug markers. The guide has a head, neck, torso, arms and legs.
  It uses the recorded avatar anchor for body placement where available
  and falls back to a clearly approximate hip offset for older v1/v2 clips.
- **REPLAY GHOST BODY: ON/OFF** toggles the new guide. Debug markers remain
  available. F2 can be hidden for a clean desktop-camera recording.
- OFFLINE tab still works without an active VR headset or server join.
- The guide renders only from RC's separate desktop camera, using GL lines.
  There are no spawned networked characters, recreated VR rigs, or remote
  player queries. Live local avatar and game cameras stay untouched.
- The generated offline reference grid now stays anchored to the **first
  recorded pose** instead of following the character, so movement can
  be inspected against a stationary reference plane.

## What is still missing (important)

**This version DOES NOT save or replay the actual textured Orbus model.**
The pre-existing `ShowLocalAvatar` toggle merely lets RC's desktop camera
display the avatar *already being simulated in a live server session* by
including its Minicam rendering layer.

A usable offline recreation will also need to map installed avatar assets,
equipment and materials, record sufficient bone/animation state and safely
instantiate a render-only replica with no gameplay/network behaviours.
The v3 anchor and rig manifest are the foundation for this research, not
a claim of complete mesh capture. The first recording may report zero valid
avatar anchors if the local character rig is not resolved yet.

## Testing

1. Publish v0.9.21 via the existing local RC `release.ps1`, then install
   the GitHub release through RebornManager.
2. In VR, join a normal world and confirm the local avatar is visible using
   RC's existing third-person or freecam setting when appropriate.
3. Keep the buffer enabled and let it collect at least 20–30 seconds of
   movement (walk, turn, raise/lower arms).
4. Press `Ctrl+Shift+C` to save. Under the **selected RM game instance**,
   look in `BepInEx/config/OrbusRebornCamera/Clips/` for:
   - `RC_Clip_YYYYMMDD_HHMMSS_<id>.tsv`
   - `RC_Clip_YYYYMMDD_HHMMSS_<id>.avatar.txt`
5. Inspect the TSV header: it should say `rolling pose clip v3`.
   Check the `avatar_valid` column for 1s (may be 0 if no rig identified).
   Check for `[AvatarCapture]` in BepInEx logs if no anchors were found.
6. You can then leave the server, or disconnect the headset, open F2 ->
   OFFLINE -> Start Offline Preview, and use REPLAY to load/scrub the clip.
7. The cyan ghost's body should follow the recording. Toggle it via
   **REPLAY GHOST BODY**. With an old v1/v2 clip you should still see an
   estimated body derived from head/hand poses, without requiring a resave.

**No real-model playback yet.** If the rig is missing, share the
`*.avatar.txt` descriptor and BepInEx `[AvatarCapture]` lines (the report
contains local Unity asset names; examine before posting publicly).

## Release command from any other PowerShell project folder

```powershell
$rc = @(
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1",
  "$env:USERPROFILE\Documents\OrbusRebornCamera"
) | Where-Object { Test-Path (Join-Path $_ "release.ps1") } |
    Select-Object -First 1
if (-not $rc) { throw "Could not find your RebornCamera folder." }
& (Join-Path $rc "release.ps1")
```

The GitHub Actions tests compile the isolated recorder/reader/guide logic
without actual Unity game binaries; only the local release script compiles
against the real Orbus IL2CPP references. If a real build fails, please send
the compile error and we'll correct it. The GitHub automatic release workflow
still requires private build-reference configuration.
