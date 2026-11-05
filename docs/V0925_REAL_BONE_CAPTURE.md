# RebornCamera v0.9.25 — real pet/NPC bone animation capture

## Goal and what was actually implemented

The world grid fix from v0.9.24 remains. This update moves beyond
recording nearby actors as moving axes: it records the world-space
positions **and rotations of the actual skin bones** already being
animated by the local Orbus client, for up to four nearby actor rigs.
It can display the saved bone hierarchy when replaying without a server.

**This is NOT full textured character replay.** RC does not yet export
mesh vertex buffers, UVs, textures, clothing state, prefab dependencies,
animation events, HP, attacks, spells, NPC AI or live voice. Bones are
recorded correctly only if the actual client makes those actor rigs
available and the local frame capture succeeds.

## Why target these classes?

Your v0.9.23 capture included `TigerDragonBase(317)`, a likely pet
with a 34-bone mesh, and humanoid objects such as `Randel(137)`,
`Fisher(162)` and `Auctioneer1(128)` with 52-bone skin rigs.
This release selects those kind of verified *skinned mesh renderers*
from **already discovered nearby objects**, rather than scanning the
entire world or guessing the NPC's animated body parts.

## How bone capture works

- The ordinary actor timeline remains 10Hz for pose, animation hash and
  appear/leave events.
- New bone sampling is **5Hz**, max **4 nearby animated rigs per sample**,
  maximum **72 bones per rig**. RC prefers the skinned renderer with the
  richest valid bone array (mesh vertex count breaks ties).
- The recorder records each joint's **world position + quaternion**, plus
  a one-time definition of bone paths, parent indices and mesh name.
  It does not capture geometry or materials for offline rendering.
- A bounded, 30–600-second ring buffer retains bone-pose frames along
  with the existing local player and nearby actor timelines. Sampling
  uses Unity's main thread; saving uses the background file writer.
- If an actor leaves the area, previously saved bone data remains in
  the rolling buffer. If its scene wrapper dies, RC attempts a
  low-frequency retry rather than continuously scanning.
- Ordinary gameplay and the XR rig are never changed. No remote
  gameplay entities are spawned, network messages sent, or player
  identities inferred.
- RebornMenu automatically exposes
  `ReplayBuffer.RecordNearbyRigBones` (default **true**) alongside the
  earlier `RecordNearbyActors` and `NearbyActorRadius` options. Switch
  off bone capture if it affects your VR frame rate.

## Files created when you save Ctrl+Shift+C

The existing `RC_Clip_...tsv`, `.avatar.txt`,
`.actors.tsv` and `.actors.manifest.txt` remain intact.

If any nearby valid rigs were captured, a new
`RC_Clip_....rigs.bin` is saved alongside those files. This versioned
binary file includes clip/session IDs, bone-path topology, and timestamped
bone poses. It is kept separate from the v4 pose TSV to preserve old clip
compatibility and reduce file size.

It will **not** appear for v0.9.24 clips, or if the detector observes no
valid skinned rig during the new capture window.

## How to test

1. Publish RC v0.9.25 from GitHub via the normal local `release.ps1`;
   update it in RebornManager.
2. In VR, enable your rolling buffer and stand near your pet or an NPC
   performing an animation. Wait 20–30 seconds, move/turn and keep the
   animated actor in sight. Save with **Ctrl+Shift+C**.
3. Check the chosen RM game instance's
   `BepInEx/config/OrbusRebornCamera/Clips/` folder for a new
   `RC_Clip_....rigs.bin` file. It is intentionally binary; don't open
   it in a text editor. The log should contain
   `[WorldRigs] Found actor-... (...), NN real skin bones` for successful
   detections.
4. Leave the server and VR if desired. Open Orbus at the main menu,
   press F2 -> OFFLINE -> START OFFLINE PREVIEW.
5. Under REPLAY choose **Load latest saved clip**, then click the
   **ACTORS** button until it says `ACTORS: BONES` (the three modes are
   OFF, AXES and BONES).
6. Press Play or scrub the recording. Cyan joint connections represent
   the actual recorded skin bone positions for NPC/pet candidates;
   magenta connections represent candidate remote-player rigs.
   If the binary sidecar is absent, only actor position axes are shown.
   Actors remain hidden when they are not observed in the main actor
   timeline; we do not leave them frozen indefinitely.
7. Send the new **.rigs.bin**, matching **.actors.tsv**, the
   **.actors.manifest.txt**, and `[WorldRigs]` log entries for analysis.
   A short video of the bone playback will help show whether the pet and
   NPC movements match your memories of the live session.

## Critical limitations

- Rig bones provide truthful movement, not a rendered skin. Offline
  character meshes and equipment need an asset-loading / render-only
  reconstruction phase. The file stores skin bone paths and transform
  observations, not skin weights, textures, bind poses or material shaders.
- Bone sampling is capped at 5Hz. Interpolation helps for smooth playback,
  but it will not capture every nuance of very fast combat motions.
- Only actor candidates already visible via the bounded physics survey
  can be recorded; distant NPCs, hidden rooms and actors with no detected
  collider are not included.
- Additional rig records may increase memory and save size; the binary
  reader caps files at 50MiB and validates actor/bone counts before loading.
- Actor IDs remain session-local; Party Sync still needs verified player
  identities, clocks, zones and merge logic before any two clients'
  observations can be combined into one coherent replay.
- No voice, chat or other personal audio is recorded. Future voice capture
  will require deliberate opt-in from participants.
- Tests validate binary codec, multiple actor tracks, teleport handling,
  bounded source compilation and stereo-camera-safe render code against
  Unity stubs. They do not establish that the runtime can read every
  Orbus mesh or recreate a texture. The local `release.ps1` build against
  actual game interop remains the required next step.

## Release command from any current PowerShell directory

```powershell
$rc = @(
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1",
  "$env:USERPROFILE\Documents\OrbusRebornCamera"
) | Where-Object { Test-Path (Join-Path $_ "release.ps1") } |
    Select-Object -First 1

if (-not $rc) { throw "Could not find the RC folder." }
& (Join-Path $rc "release.ps1")
```

The GitHub automatic publisher may still require private build
references; the existing local release script can use your actual
installed BepInEx/Orbus Unity interop and then publish the DLL for RM.
