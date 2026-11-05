# RebornCamera v0.9.24 — location-aware offline grid

## What went wrong in v0.9.23

The offline grid was a fixed 20-by-20-metre patch anchored under the
**first frame** of the pose clip. When an existing clip began around the
character-selection location and then teleported into Highsteppe, the
markers followed the correctly recorded world coordinates but the
reference grid remained at the original position, sometimes over a
kilometre away. This was a VIEWER bug, not lost pose data.

## Fix

- `ReplayClipData` precomputes a grid reference for each continuous
  location segment. Teleports and verified zone changes start a new
  segment; ordinary walking and short recording gaps do not.
- The offline viewer uses the current segment's starting headset height
  to place the diagnostic plane. This is **not terrain sampling**.
- Instead of a finite 20m grid which disappears whenever the character
  travels more than ten metres, it draws a 60m tiled window near the
  recorded headset. Every line stays on an **absolute integer world
  coordinate**; the visible window advances in 10m increments to expose
  more lines. The grid does **not translate smoothly underneath the avatar**.
- A clip recorded across a teleport will show its reference plane in the
  new location as soon as playback reaches the teleport. Camera position
  remains under the editor's control: press **Focus recorded head** after
  seeking to another location if the freecam itself is still far away.
- No change to saved world positions, zone metadata or old clip versions.
  Old v1/v2/v3/v4 pose clips, and optional v0.9.23 actor sidecars, remain
  compatible. No new recording required.
- A new isolated regression test checks that grid references switch at
  a teleport and zone transition but stay fixed during walking/gaps.

## Confirming actor capture

The v0.9.23 actor probe may discover **pets**, NPCs, props and animation
controllers, not just other players. `animated_world_candidate` is
deliberately an uncertain category: it is not proof of an NPC, and
a collection of actors does not establish a verified player name.
As a result, even with the grid fix:
- Remote-player names are not yet captured.
- Actual NPC renderable models, animation bones, health and interactions
  are not replayed. Current actor playback is a position/orientation marker.
- The world remains a diagnostic grid, not actual Highsteppe scenery.

## Test without VR or a server

1. Publish the new version with the existing local `release.ps1`
   workflow and update it in RebornManager.
2. Start Orbus at its main menu without wearing VR.
3. Open F2 -> OFFLINE -> START OFFLINE PREVIEW.
4. In REPLAY, **Load latest saved clip** and play or scrub a recording that
   crosses a zone change / teleport. The earlier clip can be reused.
5. At each recorded location the grid should now be near the replayed
   head and hands. During ordinary movement, the lines remain world-aligned.
6. Press **Focus recorded head** if the freecam view is left behind when
   you jump to a distant position on the timeline.
7. Toggle **OTHER ACTORS** if a matching `.actors.tsv` exists.

## Local release from any PowerShell directory

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

GitHub's isolated validation uses synthetic clip tests; actual Unity
rendering must be checked with the user's installed Orbus interop build.
