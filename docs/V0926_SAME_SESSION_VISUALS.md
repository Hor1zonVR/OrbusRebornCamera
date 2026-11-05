# RebornCamera v0.9.26 — first actual Orbus mesh rendering prototype

**Status:** pushed source, Unity/Orbus IL2CPP runtime not yet validated.
This is a deliberately bounded VISUAL experiment, not the final
Minecraft Replay Mod offline world solution.

## What changed

v0.9.25 demonstrated 1,304 readable actor skin-bone samples,
including a 34-bone TigerDragon pet and 52-bone humanoid NPC rigs,
with real skeleton playback while disconnected.

This update makes the first attempt to **draw real textured Orbus game
models**, not just coloured line skeletons:

1. While VR is running, RC makes a small COPY of the local player's
   visible body, shoulder, face/hair/helmet, and hand meshes and their
   original materials, using the proven `PlayerRigv4` hierarchy. Hand
   geometry is initially baked at the moment of capture; hand **finger
   animation is not yet recorded** by the local v4 clip format.
2. When it finds a nearby skinned NPC or pet for rig capture, RC retains
   a duplicate of the *actual loaded Unity Mesh* and its Materials,
   alongside the existing bone topology/animation samples.
3. In F2 -> OFFLINE, new **render-only** GameObjects are constructed
   from those cached copies. NPC/pet skin bones are driven by the
   existing `.rigs.bin` timestamps. No gameplay prefabs, NPC AI, user
   controllers, networking scripts, or physics are instantiated.
4. **ACTORS: MODELS** is a new playback display mode. The cycle is
   OFF -> AXES -> BONES -> MODELS -> OFF. Visuals not present in the
   current process fall back to diagnostic axes.
5. Offline camera only renders RC visual objects on dedicated layer 29.
   Regular game objects remain culled. The old black stage and world-
   aligned grid remain as non-persistent debugging aids.
6. An OPTIONAL `ReplayBuffer.CacheNearbyWorldGeometry` config setting
   (default **false**) can copy nearby static MeshFilter/MeshRenderer
   geometry from objects already observed through the existing
   physics-neighbourhood survey. Caps: 96 meshes, total 450,000 source
   vertices, maximum 4 new meshes per survey. Static meshes are only
   previewed at their original world positions and within their
   recorded zone. The OFFLINE tab adds **CACHED WORLD ON/OFF**.
7. All copied geometry is **in-memory / SAME PROCESS ONLY**. No mesh
   or material binaries are yet saved inside replay files. This avoids
   claiming that old recordings can reconstruct a whole game world.

## Important limitations

- This may render nothing or show incomplete/misaligned skins if Unity
  IL2CPP material cloning, object layer selection, skin binding or
  asset lifetime differs on the actual Renewed/Spades runtime.
  GitHub tests only compile against Unity stand-ins. **Real in-game
  testing is required before calling this working model replay.**
- **After fully restarting Orbus**, cached visual assets are gone,
  so an old `.rigs.bin` recording can still show its skeleton but not
  the actual model. The next durable solution requires resolving
  installed Unity asset sources/prefabs or serialising visual assets.
- The initial character cache records appearance when first found;
  it does not track later equipment/appearance changes.
- Local avatar hands may have static finger poses; body/head/hands
  otherwise follow their recorded v4 anchors, not full animations.
- The single skinned model chosen from a multi-mesh NPC/pet may omit
  separate accessories and face parts. This is not yet a whole
  character appearance snapshot.
- The static world prototype **does not load scenes offline**, may
  miss Terrain, decorative scene meshes without colliders, or
  objects not reached in the first 256 collider survey results. It
  does not reconstruct lighting, shadows, terrain, particles, water,
  collisions, scripted doors or NPC interactions.
- No voice chat or gameplay event capture has been added.
- This is non-networked: no server/zone switches, no actor manipulation.
- If VR performance suffers, disable `CacheNearbyWorldGeometry`
  in RebornMenu (default OFF). The existing marker/skeleton viewer
  remains usable without the new visual cache.

## How to test — do NOT restart Orbus between live and offline

1. Publish v0.9.26 using the local `release.ps1` and update via
   RebornManager. Confirm BepInEx reports v0.9.26. If the real IL2CPP
   build fails, share the exact compiler error before testing further.
2. Open RebornMenu -> RC configs -> ReplayBuffer.
   Leave `RecordNearbyActors` and `RecordNearbyRigBones` ON.
   For the scenery experiment, explicitly turn
   `CacheNearbyWorldGeometry` ON. It is OFF by default.
3. Join Highsteppe in VR, stand near the TigerDragon pet and/or
   Randel/Fisher. Walk about for 30-60 seconds. The log should include
   `[VisualCache] Copied ...` and optionally
   `[WorldVisuals] Cached ...`.
4. Ctrl+Shift+C to save the clip. **Leave the server but keep Orbus
   RUNNING**. If the game exits/restarts, the visual cache is lost.
5. F2 -> OFFLINE -> Start Offline Preview -> REPLAY ->
   Load latest saved clip. Cycle ACTORS to **MODELS**.
   The local avatar and any cached/recorded NPC/pet rigs should try
   to render with original geometry and materials.
6. In OFFLINE, toggle **CACHED WORLD ON** to inspect the copied static
   mesh subset. If it is empty despite enabled recording, check
   `[WorldVisuals]` logs. The background grid is normal; only a
   bounded portion of scenery is included.
7. Compare against ACTORS: BONES. Note exactly which mesh renders,
   whether animation drives it, any invisible geometry, and whether
   the scene cache survived returning to the main menu.
8. If a visual is missing, share BepInEx `[VisualCache]`,
   `[WorldVisuals]` and `[WorldRigs]` log lines, as well as the
   matching `.rigs.bin` and `.actors.tsv`. This is a single targeted
   verification before implementing portable visuals.

## Existing local release command

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

## What's after this

The next major stage is **portable visual replay**: identify the exact
asset bundle/resource/scene identity for each body/pet/world mesh and
its materials, so the offline viewer can safely load only the visual
components with no server session even after restarting the game.
That also requires layer/renderer validation against the actual IL2CPP
runtime and an asset dependency resolver. Party Sync will then merge
multiple clients' world/actor observations with verified identity,
zone and time alignment, retaining footage after disconnect.
