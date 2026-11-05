# RebornCamera v0.9.22 — Targeted real local-avatar transforms

This version corrects the v0.9.21 failure (565 recorded frames, ZERO
valid local-avatar anchors) and begins gathering data from the *actual*
Renewed/Spades player hierarchy. It does **not** yet render a genuine
mesh offline or record bones at animation frame rate.

## Research grounding

The older AvatarInspector logs recorded concrete Unity object paths:
- `PlayerRigv4/Avatar/Torso/BodyParent` (layer 20 Minicam)
- `PlayerRigv4/Camera (eye)/Head`
- `PlayerRigv4/Controller (left)/Grabber/Hand/model`
- `PlayerRigv4/Controller (right)/Grabber/Hand/model`
- `PlayerRigv4/Dummy/Dummy` with an inactive mesh and 56 bones
- remote player examples under `OtherPlayerv2(Clone)`

The previous recorder incorrectly looked for a skinned mesh renderer *on*
the Minicam layer. That missed the actual `BodyParent` Transform and
other player meshes on Default. Some active visual components have zero
bones, while skinned hands have separate 20-bone hierarchies.

The user also identified a functioning native PlayerMenu hook
(`ng.sew`) via Renewed's IL2CPP dump. That UI discovery is not a
substitute for an avatar object reference. The Renewed dump includes
`MagicalActual.AvatarAppearance` fields (hairstyle, haircolor, body
morph values), but we have **not** established which actual runtime
instance owns those values for the local player, nor linked the
Community/obfuscated responsibilities for avatar setup. We won't
guess that mapping or use obfuscated symbols as a universal API.

## Changes

- `LocalAvatarCapture` no longer scans all scene SkinnedMeshRenderers.
  It starts at the actual source eye's ancestor named `PlayerRigv4`,
  then follows explicit local-only paths to the root, body anchor,
  head and hands. If a path is absent, it records an invalid pose
  and a precise reason; it never selects a random remote avatar.
- A **v4 clip** contains five local hierarchy pose tracks when available:
  `avatar` (BodyParent), `avatar_root` (PlayerRigv4),
  `avatar_head` (model head), `avatar_left` / `avatar_right`
  (rendered hand models). Headset and XR controller tracks are
  unchanged. Each has separate validity flags.
- On each Save Clip, `*.avatar.txt` contains the resolved paths,
  body/head/hand positions and rotations, and a **bounded, targeted**
  list of renderer/mesh names, material names, vertex count hints,
  skinned renderer bones, and root bones under Avatar, Torso,
  BodyParent, Head and the two hand Grabbers.
- No skin geometry, texture pixels, mesh weights, equip state or
  whole-body per-frame bone animation is copied by this build.
  Names/asset references are *diagnostic*, not fully portable models.
- The fictitious legged ghost has been removed. The viewer has a
  `CAPTURED RIG TRACKS: ON/OFF` control; this draws only valid tracked
  transforms (not animated body geometry). Old v1–v3 clips still
  load and show their original XR debug markers.
- No ordinary settings UI is added. RebornMenu already provides native
  config discovery; RC keeps its Creator Studio for cinematic tools.
- The rolling five-minute buffer, zone metadata, offline pose preview,
  user-keyframe camera, and Save Clip behaviour are retained.

## What to test

1. Publish v0.9.22 from your existing RC clone using `release.ps1`;
   update via RM after the GitHub release succeeds.
2. Join a world with the VR headset and use RC as normal. Move, turn,
   raise both hands, and save a new clip with Ctrl+Shift+C.
3. Open the new `RC_Clip_*.avatar.txt` from the selected RM instance's
   `BepInEx/config/OrbusRebornCamera/Clips/` directory.
   Look for:
   - `capture_status=body_anchor_resolved`
   - `body_path=PlayerRigv4/Avatar/Torso/BodyParent` or equivalent
   - `part=BodyParent`, `part=Torso`,
     `skin_part=Avatar` and hand skin meshes
   - Matched material names and bone lists, or explicit missing reasons.
4. Inspect the `.tsv`: `rolling pose clip v4`; count 1s in
   `avatar_valid`, `avatar_root_valid`, `avatar_head_valid`,
   `avatar_left_valid`, and `avatar_right_valid`.
   Earlier clips *cannot* gain these channels retroactively.
5. Use F2 -> REPLAY -> Load latest saved clip. Toggle Captured Rig Tracks.
   No made-up legs should appear. Offline playback via F2 -> OFFLINE
   remains available even without the headset or a server connection.
6. If the anchor isn't found, collect BepInEx lines beginning
   `[AvatarCapture]`, plus the rig manifest and the new TSV header
   and several data rows. Do not run a broad full-scene scan.

## The next model-rendering milestone

Once the report identifies the actual local renderer and bone hierarchy
for the avatar body and equipment, map the corresponding model assets
to their source prefabs or bundles. Capture the dynamic bone/controller
state required to drive a *render-only*, local replay replica with its
gameplay and networking components absent. Validate the exact rig
appearance in live replay first, then in offline Preview. Only call this
"actual model replay" when a saved clip has genuinely restored the
visual mesh, clothing/equipment and movement after disconnecting.

## Local release from any PowerShell directory

```powershell
$rc = @(
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1",
  "$env:USERPROFILE\Documents\OrbusRebornCamera"
) | Where-Object { Test-Path (Join-Path $_ "release.ps1") } |
    Select-Object -First 1

if (-not $rc) { throw "Could not locate the RC clone." }
& (Join-Path $rc "release.ps1")
```

GitHub's isolated C# test workflow does not contain real Unity/Orbus
binaries. The local release script is the real IL2CPP-interop compilation
and GitHub publication path for RebornManager. Automatic GitHub Actions
publishing still needs configured private build references.
