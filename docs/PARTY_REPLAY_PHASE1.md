# Party Replay — Phase 1 (two-recorder local import)

**Development state:** First milestone. A two-person **pose/actor timeline**, not a
finished 3D dungeon reconstruction. No automatic network transfer, not a
complete game recording, and not a renderer for unloaded world geometry.

## How Horizon and Jordan record

1. Both install the same RebornCamera version, enable the replay rolling
   buffer in **F2 → REPLAY**, and choose **5 MIN** (or shorter).
2. Play the dungeon normally. Each person samples their own tracked headset
   and hands, local avatar anchors, and observed nearby actors/rigs.
3. At the moment worth saving, **each person presses Ctrl+Shift+C**.
   This saves their own previous five minutes. The rolling buffer keeps
   going; saving does not stop gameplay.
4. Each saved `RC_Clip_*.tsv` is accompanied by
   `RC_Clip_*.party.zip` in the same `Clips` folder. The zip includes
   the main pose TSV, available actor/rig sidecars, and optional manifests.
5. Jordan sends the **.party.zip**, not his entire game folder.
   On Horizon's PC, copy it to the *same game's*:
   `BepInEx/config/OrbusRebornCamera/PartyImports/`
6. **F2 → PARTY**: select **Load my latest clip** then **Import partner clip**.
   **Start offline stage** for the independent desktop replay preview.
   Press **Focus my replay** or **Focus partner**, turn on **Camera follow**
   for a third-person tracking shot, or turn it off and fly the freecam.
   Play/pause/seek on PARTY (or replay in REPLAY).
7. When Horizon stops early and Jordan's exported clip continues, Horizon
   disappears from the shared timeline while Jordan's recorded frames remain
   available. Scrub the combined timeline to inspect both.
8. If both machines' wall clocks disagree, use the **Partner clock correction**
   slider (+/-120 seconds). Confirm alignment on a recognizable event.

Files live under an RM game instance's `BepInEx/config/OrbusRebornCamera`.
Clip ZIPs can contain sensitive information about your playing sessions and
positions; share only with participants you trust.

## What actually merges now

- Both participants' **own** 30 Hz VR tracking/pose recordings.
- Both clients' observed nearby actors, and rig bone observations where
  the relevant sidecars exist.
- Recorded UTC timestamp alignment and explicit gaps, transitions and
  departure times.
- One freecam that you can independently move between their world-space
  recorded positions or attach to either available headset trajectory.
- Same zone labels are needed for combined spatial preview; matching labels
  do **not** prove two recordings were in the same dungeon instance.

## Current visual limitations

- The shareable ZIP does **not** contain terrain, world meshes, avatar meshes,
  textures, animation assets or voice/audio. The current offline stage still
  shows grid/axes and observed bones; any detailed model previews depend on
  the existing same-process visual cache.
- Merging world views does **not** synthesize missing terrain between the
  two participants. The near-future visual pipeline needs portable asset
  references or authorized local asset reconstruction.
- Nearby actor keys are *local Unity identifiers*, not shared global IDs.
  Spatial de-duplication is heuristic and may leave duplicate actors.
- There is **no** live synchronization to a friend's computer, no background
  copying, and no guaranteed remote player observation beyond the live
  client's interest area.
- If one player saves a short/late buffer, data from before that window is
  absent. The other recording can still show their own observations then.

## Next development milestones

1. **Prove phase 1 in a two-client test**: same in-game zone/instance,
   both save at the same encounter, import, check synchronized poses and
   no false ghosts when one player leaves.
2. **Portable character rendering**: map recorded local equipment/skins,
   bones and asset references to legitimate installed client data, then
   build animated offline character models. Do not assume the existing
   same-process cache survives a restart or file transfer.
3. **World coverage**: combine scene-identifying loaded geometry and
   player-observed regions, with explicit holes where neither client saw it.
4. **Opt-in party transfer/sync**: authenticated, session-scoped exchange
   of recording chunks, unique recorder IDs, zone/instance IDs, and clock
   offset estimates. Do not exchange login tokens or impersonate players.
5. **Session timeline**: continuous recording or multiple saved rolling
   clips across dungeon/instance changes; don't stretch five-minute buffers
   into unrecorded time.

## Diagnostics

Run `dotnet run --project tests/PartyReplay.CompileTest.csproj -c Release`
and `dotnet run --project tests/ReplayBuffer.IntegrationCompileTest.csproj -c Release`
in a cloned repo before publishing. Full BepInEx/Unity IL2CPP compilation
requires local generated interop references, which the public CI does not
currently have. Use `release.ps1` only after review and a local build.
