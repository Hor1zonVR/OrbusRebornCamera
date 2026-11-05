# RebornCamera v0.9.27 — scenery child renderers and visual-cache evidence

## Why this patch exists

The first v0.9.26 real-mesh replay test rendered only a large, sparse
piece of terrain in the offline stage. It did **not** reconstruct the
Highsteppe buildings or the complete town. The user also supplied a
valid v4 player clip, actor timeline, visual manifests and rig binary;
their data confirms that NPC/pet animation recording remains intact:

- 1,734 local player pose frames.
- 2,321 nearby actor observations.
- 11 saved animated rigs.
- 798 actual bone-pose samples.
- An observed TigerDragon (`Circle_11`, 34 bones), plus multiple
  town NPCs with 52-bone skeletons.

These files do not contain a *persistent* scene mesh export, so they
cannot reveal exactly which terrain/building renderers were cached at
runtime. The new report addresses that blind spot.

## Source changes

- The optional `ReplayBuffer.CacheNearbyWorldGeometry` collector still
  requires a *live* Unity world and stays off by default.
- v0.9.26 required a `MeshFilter` + `MeshRenderer` on a collided
  transform or up to two parent transforms. Complex buildings often
  have collider and visible geometry on separate children, so a mesh
  can be skipped even though it is visible in the game.
- v0.9.27 starts from a nearby collider (or its small parent) and walks
  **at most 3 levels of children**; **220 scene nodes per survey**,
  **8 copies per survey**, **96 cached static meshes total**, and
  **450,000 mesh vertices total**. It avoids traversing giant scene
  roots, player rigs and roots containing Animator components.
- It does not do a full-scene `FindObjectsOfTypeAll` scan.
- It skips character-selection scenes so they cannot exhaust the
  small cache before Highsteppe loads. Only a few very large ground
  pieces can consume the total vertex budget; more slots remain for
  smaller architectural mesh objects.
- If a matching same-process character cache exists when opening
  the offline viewer, RC starts in `ACTORS: MODELS` mode automatically.
  The `BONES` and `AXES` modes remain available for comparisons.
- Existing saved local pose and actor bone streams remain compatible.
- When you **Save Clip**, a new
  `RC_Clip_....visuals.txt` report is written, containing:
  session cache match, source mesh names, target hierarchy paths,
  world zone, vertex counts, original positions, and counts of
  cached local avatar and NPC meshes.
- The OFFLINE tab displays how many world, avatar and actor mesh copies
  are currently cached in memory. The REPLAY tab retains
  `ACTORS: OFF / AXES / BONES / MODELS`.
- Changing actor display mode now also logs the mode and cache counts
  under `[ReplayVisuals]` for diagnosing invisible models.

## First test

1. Publish v0.9.27 through the existing local `release.ps1` workflow,
   then update via RebornManager.
2. In RebornMenu enable
   `ReplayBuffer.CacheNearbyWorldGeometry`. This is optional and
   **OFF by default**; turn it off if performance suffers.
3. Enter Highsteppe in VR. Walk through a market area with multiple
   buildings, stalls and NPCs for 45–60 seconds. Summon the TigerDragon
   if possible. Do **not** just stand beside a large terrain mesh.
4. Save a clip with Ctrl+Shift+C.
5. Check `BepInEx/config/OrbusRebornCamera/Clips/` for the new
   `RC_Clip_....visuals.txt`. Copy this report for analysis.
6. Leave the server **without restarting Orbus**. In F2 -> OFFLINE
   start the detached preview, load the new saved clip, and turn
   `CACHED WORLD: ON`.
7. In F2 -> REPLAY, explicitly click until the actor control reads
   **`ACTORS: MODELS`**, not `ACTORS: BONES`. The latter shows
   skeletons instead of mesh proxies. See whether the dragon or any
   NPC model renders and animates.
8. If the buildings are still absent, send the `.visuals.txt` and
   BepInEx `[WorldVisuals]`, `[VisualCache]` and
   `[ReplayVisuals]` log lines. These differentiate discovery failure,
   source mesh lifetime issues, material/culling problems, and
   failure to match the current recording session.

## What is still not implemented

This is NOT a complete Highsteppe scene loader and is NOT the promised
portable world-asset loading milestone. The collector still misses
static objects without suitable nearby colliders, Terrain components
that aren't MeshFilters, some LOD/decorative objects, and content beyond
its strict limits. It does not reproduce buildings after restarting
Orbus, nor persist mesh geometry/texture data to the replay clip.

The longer-term solution is resolving the required Unity scene and
asset dependencies from the installed game, then instantiating ONLY
render-only components in a detached offline scene. It should not run
gameplay scripts, network controllers, physics, or NPC AI.

The actor sidecars have actual bone movement and can eventually drive
full models if a consistent mesh/material/skin binding can be loaded
offline. They still do not capture player names, voice, damage,
projectiles, or complete NPC interactions.

## Release from any PowerShell directory

```powershell
$rc = @(
    "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
    "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1",
    "$env:USERPROFILE\Documents\OrbusRebornCamera"
) | Where-Object {
    Test-Path (Join-Path $_ "release.ps1")
} | Select-Object -First 1
if (-not $rc) { throw "Could not find the RC folder." }
& (Join-Path $rc "release.ps1")
```

The public GitHub isolated tests do not prove the compiled DLL works
against Renewed's real Unity IL2CPP interop. Only the local release
script can validate that runtime, followed by in-game testing.
