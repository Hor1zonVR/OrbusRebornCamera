# RebornCamera v0.9.23 — nearby actor/world observation prototype

## Goal

Capture **what the local game client actually observes nearby**, rather
than re-simulating a server or treating faraway/unobserved rooms as recorded.

This build does NOT yet reproduce real world geometry, full NPC AI, spells,
combat events, 3D player models or voice chat. It adds a first bounded timeline
of nearby other-player and animated-object observations that can be inspected
offline alongside the existing local player/VR pose recording.

## How recording works

- The existing five-minute local-player pose ring remains unchanged.
- When the normal buffer is enabled and VR tracking is active, an additional
  read-only subsystem performs a Unity Physics neighborhood query about once
  every 1.35 seconds; it samples already discovered actor transforms at about
  **10 Hz**. Neither it nor the offline viewer sends server requests.
- The survey covers **30 metres by default**, configurable between 10 and
  50 metres using RebornMenu's automatic BepInEx config discovery:
  `ReplayBuffer.RecordNearbyActors` (default true), and
  `ReplayBuffer.NearbyActorRadius` (default 30).
- The survey checks at most 256 nearby colliders and tracks at most 24 actor
  objects concurrently. It does not globally enumerate all game objects.
- Known `OtherPlayerv2(Clone)` roots are tagged as
  `remote_player_candidate`; other observed Animator-bearing roots are
  tagged `animated_world_candidate`. A candidate may be a prop or
  interactive object rather than an NPC. An object without a collider or
  resolvable Animator may not be captured.
- A new session-local key is assigned when a live actor object is discovered.
  This key is NOT a verified game account/user ID and cannot be used to
  safely merge recordings from different PCs yet.
- The recorder saves position, rotation, event (appear/pose/leave), optional
  Unity Animator layer-0 state hash/normalized time, clip timeline seconds,
  UTC ticks, and current loaded-zone signature. When actors are no longer
  nearby, a `leave` event stops their display. Older observations survive
  object despawn or removal from the live-tracking set.
- Actor states are **observations** and can have network interpolation, lag,
  load/unload gaps and intermittent visibility. No enemy HP, attacks,
  projectile paths or animation bone arrays are reconstructed by this build.
- Display names are left **blank intentionally**. We must first identify a
  verified remote-player name/identity field in the actual Renewed runtime.
  Never assign a real player's name based on a nearby prefab.
- Voice/audio/chat capture is NOT enabled. Future voice recordings require
  per-participant consent and synchronised tracks.

## Files on Save Clip

Alongside the original local-pose recording:

1. `RC_Clip_....tsv` — existing v4 local head/hand/body/rig streams,
   unchanged and compatible with previous replay files.
2. `RC_Clip_....avatar.txt` — existing local character visual manifest.
3. `RC_Clip_....actors.tsv` — NEW actor observations from the SAME clock
   window and clip/session IDs as the local pose file.
4. `RC_Clip_....actors.manifest.txt` — NEW targeted read-only mesh/material
   inventory of the observed actor objects still nearby at the moment of
   Save Clip (not reconstructed model data).

All writes occur **only when you save a clip**; recording state remains in
memory until then. No data is automatically uploaded or shared.

## Offline viewer changes

- F2 -> REPLAY -> Load latest saved clip loads the normal pose TSV and, if
  present, the adjacent actor TSV. The latest-file selector explicitly
  excludes `.actors.tsv` so it cannot mistake that file for a pose clip.
- Remote-player candidates display as **pink/purple axes**; animated-world
  candidates display as **orange axes** on the desktop recording camera.
  `OTHER ACTORS: ON/OFF` toggles those markers. They are intentionally
  markers, NOT recreated character/NPC meshes.
- The viewer interpolates short contiguous position samples and hides actors
  after a leave event or prolonged missing samples. Thus nearby NPCs are
  NOT simply frozen forever on an empty offline stage.
- Existing offline stage still has a placeholder grid only; **no saved
  Highsteppe/dungeon scenery is loaded**.

## First useful in-game test

1. Run the normal RC release workflow for source v0.9.23, install DLL via RM.
2. Join a world in VR and stand near a **visible other player or a clearly
   moving NPC** for at least 20 seconds, then move away for a few seconds
   and return. In a separate test, move with a friend or NPC in sight.
3. Save with Ctrl+Shift+C. Check the actor sidecar has nonzero `sample`
   lines and rows such as `appear`, `pose`, and `leave`.
   The in-game physics query may return zero candidates when actors lack
   nearby colliders or animation components; if so, that's diagnostic
   information, not proof the actors don't exist.
4. In offline replay, load latest clip, toggle `OTHER ACTORS`, scrub the
   recording. You should see observed actor markers only when recorded.
5. Share the `.actors.tsv`, `.actors.manifest.txt` and the BepInEx lines
   beginning `[WorldActors]` to help map real player/NPC hierarchies.

## Why this isn't complete world-state replay yet

Static geometry belongs to the **installed Unity assets and scenes**.
Dynamic state requires recording all relevant objects and events *within
the area observed by each contributing client*, e.g. NPC movement/animation,
damage, target selection, spawn/despawn, weapons, spells and interactables.
The next research step is to resolve the game-specific NPC and remote-player
component classes using the readable Community dump as an explanatory
reference and then locate their actual Renewed/Spades equivalents. Only then
can we capture verified enemy health, actions and appearance.

For Party Sync, recordings will need a shared session ID, trusted player
identity mapping, clock-offset correction, coordinate/instance alignment
and a merge algorithm that preserves samples from clients that disconnect.
This v0.9.23 actor key is explicitly **not** that shared identity.

## Release from any PowerShell working directory

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

The GitHub test workflow validates sidecar round-tripping and isolated
source compilation, but the actual Unity IL2CPP build/runtime requires
the user's installed game references. The automatic GitHub publisher still
requires its private build-reference setup; the normal local release
script is the path to RM.
