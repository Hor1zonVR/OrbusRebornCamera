# RebornCamera v0.9.18: rolling pose replay buffer

## What this release implements

- A rolling in-memory capture of **local headset + left/right controller poses**.
- **Five minutes (300 seconds)** by default, sampled at a target 30 Hz.
- Time-window and capacity limits: no unbounded list, no continuous file writes.
- **Ctrl+Shift+C** saves the buffered samples as a permanent .tsv clip.
- **F2 Creator Studio → RECORDING** has Save Clip, on/off and 1/3/5-minute
  presets. Config also allows values from 30 to 600 seconds.
- A Save Clip operation clones the in-memory buffer first, then writes from a
  background worker. Capture is not paused while the file saves.
- Clips preserve a session identifier, clip identifier, UTC sample ticks, local
  time offset in the active game process, scene names, and validity flags for
  missing controller tracking. This prepares the file layout for future
  synchronization, but DOES NOT IMPLEMENT PARTY SYNC.
- If the VR eye temporarily disappears during zone loading, capture skips
  those samples instead of generating a fake world pose. Stale frames expire.

**Not yet implemented:** replay playback, other players or enemies,
animations, spells, world/entity reconstruction, actual gameplay events,
five-minute playable clips or party-merged replay files.

## Install / publish with the existing RM workflow

The v0.9.18 source is pushed to GitHub main. The automatic GitHub Actions
publisher currently needs private game/BepInEx references configured and may
fail its preflight; that failure does not mean this source failed to compile.

From any PowerShell terminal, even inside a different VS Code project:

```powershell
$rcCandidates = @(
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera",
  "$env:USERPROFILE\OneDrive\Documents\OrbusRebornCamera-1"
)
$rc = $rcCandidates | Where-Object {
  Test-Path (Join-Path $_ "release.ps1")
} | Select-Object -First 1
if (-not $rc) { throw "RC clone not found in either OneDrive Documents folder." }
& (Join-Path $rc "release.ps1")
```

The script pulls the latest main branch, builds with local game IL2CPP
interop, publishes BetterMirror.dll as GitHub release v0.9.18, which RM can
then discover. It stops safely if there are uncommitted tracked local changes.
The full game DLL still needs a successful local build and in-game verification.

## Test in-game

1. Launch Orbus using the modded instance from RM after publishing/updating.
2. Leave the default [ReplayBuffer] Enabled=true; wait at least ~15 seconds
   in a loaded world with VR tracking available.
3. Press F2 and select RECORDING. The buffer should report its available
   duration and accumulated pose frame count.
4. Press Ctrl+Shift+C, or click SAVE LAST 5 MINUTES.
5. Look in the **active RM game instance**:
   `BepInEx/config/OrbusRebornCamera/Clips/RC_Clip_*.tsv`
6. Inspect the header: `content=POSE_ONLY_NO_WORLD_RECONSTRUCTION`,
   session_id, clip_id, first/last UTC ticks, sample count.
7. Save a second clip without resetting the recording buffer. Make sure
   the frame count continues. Switch zone, return, check it records after
   tracking returns without touching the player.
8. Optionally disable the buffer. It immediately clears unsaved pose history.

When there is an error, send the BepInEx LogOutput.log lines beginning
`[ReplayBuffer]`, and whether the expected .tsv file exists.

## Data and performance notes

- Captures only local VR poses. No automatic video, enemy tracking,
  chat, account credentials, or remote player positions are captured.
- Holds at most (duration*30+2) frames, around 9,002 frames at five minutes.
- Save Clip writes one temporary `.partial` file and renames after success.
  On an abnormal crash, a partial file may remain. Files are kept until the
  user manually deletes them; the rolling RAM buffer itself never grows.
- A maximum of two clip saves may be queued concurrently.
- Captured monotonic seconds are local to this run; UTC ticks are metadata,
  not a network clock-synchronization protocol.
- The old manual Ctrl+Shift+R 60-second diagnostic remains available
  separately and isn't needed for normal clipping.
- A per-game/instance-only setting protects vanilla/public servers because
  the recorder makes no requests to move, reveal or replicate gameplay.
