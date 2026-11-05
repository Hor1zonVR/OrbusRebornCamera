# RebornCamera v0.9.28 — Highsteppe ground, walls and building renderer pass

## Why this is needed

v0.9.27 successfully rendered some actual Highsteppe scenery and multiple
real character meshes in a same-process offline preview. However the
captured scene was still almost entirely **trees, props, signs and a
couple of doors** — not the Highsteppe floor, walls or buildings.

The user's `RC_Clip_20261105_152824_0e12b55a.visuals.txt` reports
**30** world mesh copies, **14,794** vertices, with the same tree
captured multiple times as `LOD0`, `LOD1`, `LOD2`. It contains tree
hierarchies under `CenterCourt` and `FellowshipCourt`, and separate
roots such as `UndergroundStatic` and `Nate_HS`.

This report supports the idea that RC is copying actual world assets,
but **does not prove the floor/walls are MeshFilters**. Those might
be larger combined meshes, scene components with distant transform
pivots, `Terrain` components, or streamed differently.

## Changes in v0.9.28

**New structural scene pass**, in addition to the existing collider
subtree survey, under the existing *opt-in*
`ReplayBuffer.CacheNearbyWorldGeometry` config:

1. Probe exact known scene container names from the original cache
   report, including `CenterCourt`, `FellowshipCourt`,
   `UndergroundStatic`, `Nate_HS`. Optional extra likely names are
   probes only; no assumption that they exist.
2. Read **only the root GameObjects** from up to 12 already-loaded Unity
   scenes, inspect up to 1,200 root names, and select likely
   Highsteppe/terrain/architecture container roots.
3. Walk selected hierarchies incrementally: **160 nodes per pass**,
   **12 copies per pass**, maximum depth **11**, maximum queue
   **9,000**, and no global `FindObjectsOfTypeAll` scan.
4. Prefer `LOD0` and skip `_LOD1/2/3` variants; limit foliage meshes
   to 12 copies so trees stop consuming the entire small world cache.
5. Replace distance-to-transform-pivot with distance-to-the-renderer's
   **actual world-space bounds**; large walls may have remote pivots
   but still overlap the player's observation radius.
6. Allow candidate floor/wall/roof/ground/building/terrain meshes up to
   **120,000 vertices**. Previously all world meshes above 20k were
   rejected. Keep bounds: at most **128 meshes total**, **750,000
   vertices total**, no more than 7 meshes above 14k vertices.
7. Resurvey at 25-metre travel intervals or after about 35 seconds if
   the previous queue has drained, helping discover scenery encountered
   later in the same zone.
8. Save added evidence in the existing `RC_Clip_....visuals.txt`:
   `structural_meshes`, `foliage_meshes`,
   `lower_lods_skipped`, `matched_loaded_scene_roots`,
   `structural_scene_nodes_seen`, remaining queue and zone.
   Log scene probe and copies under `[WorldStructure]`.

All geometry is still **copied into memory only**, as actual
MeshFilter/MeshRenderer meshes and materials, without gameplay
components, server actions or modifying live characters.

## Focused in-game test

1. Publish the updated source with the local `release.ps1` then install
   v0.9.28 via RebornManager. If the real IL2CPP build fails, paste the
   exact compiler error. Isolated CI stubs are not authoritative.
2. Keep `ReplayBuffer.CacheNearbyWorldGeometry = true` in
   RebornMenu. It is OFF by default due to VR load.
3. Record **while walking by building exteriors, doorways, floors
   and walls** in Highsteppe, not mostly trees, for 45–60 seconds.
   Do not restart Orbus after saving.
4. Save Ctrl+Shift+C and send
   `RC_Clip_....visuals.txt`. Check whether `structural_meshes`
   is nonzero, and whether world mesh names/path include floors,
   walls, roofs, roads, bridges or building parts.
5. Open F2 -> OFFLINE -> start offline preview. Load the latest clip,
   turn `CACHED WORLD: ON`; if characters are in the same-process
   visual cache, `ACTORS: MODELS` will be selected automatically.
6. Compare against the live Highsteppe region. Screenshots are useful.
   Include `[WorldStructure]`, `[WorldVisuals]` and
   `[ReplayVisuals]` log lines if scenery is still missing.

## What this is **not**

It is *not* a complete Highsteppe geometry or asset loader, *not*
persistent scene replay after a full game restart, and does not yet
render Unity Terrain components that do not expose MeshFilter/MeshRenderer,
or reconstruct water, decals, particles, lights, skyboxes, occlusion
or streaming dependencies. RC still cannot populate a part of the
world that was never observed by a contributing client.

If this targeted structural pass still finds no floors/walls,
the correct next step is to locate the real zone's **Terrain,
scene-bundle and static-mesh rendering implementation** using the
available Unity assets and IL2CPP dumps, rather than indefinitely
widening generic scans or copying more unrelated props.

## Release command

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

This source's CI validates C# integration against Unity stubs; real
Unity IL2CPP and offline rendering require the local Orbus release test.
