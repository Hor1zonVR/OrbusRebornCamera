# Automatic Party Replay Sync — Experimental Relay (v0.9.33)

The v0.9.32 **manual ZIP transfer** was a prototype, not the intended
user workflow. The opt-in v0.9.33 transport performs the same transfer
automatically in the background, once two consenting recorders are paired.

> **Status:** First implementation. Party membership is identified by an
> explicit private relay room rather than the game's party packets. The
> relay does not record the Orbus world; it transfers each camera client's
> existing saved pose/actor/rig clips. Never point it at GameMaster's admin
> endpoint or share a GameMaster token.

## One-time room setup (host PC)

Requires Node.js 20+ (the relay has no third-party dependencies).

From the cloned OrbusRebornCamera repo:

\`\`\`powershell
node .\party-sync-relay\server.mjs create-room
\`\`\`

This prints a **Room ID** and **Room secret**. Share both privately with
Jordan, and don't put them in screenshots, public streams, Discord
announcements or logs.

Start the dedicated relay:

\`\`\`powershell
node .\party-sync-relay\server.mjs
\`\`\`

By default it listens on **127.0.0.1:4788**. For another PC or internet
participant, publish it through a **separate HTTPS reverse proxy/tunnel**.
Do not open it to the internet over unencrypted HTTP; the client rejects
non-loopback HTTP. Do not expose GameMaster's privileged port 4177.

Store the private room and its clips outside any git-tracked path if
possible. Default local data is ignored by this repo in \`.gitignore\`.

## Recorder configuration (both computers)

After installing RC from RM, set these values in the BepInEx configuration
(or RebornMenu's mod config, where supported):

\`\`\`ini
[PartySync]
Enabled = true
RelayUrl = https://your-private-relay.example
RoomId = <the 32-character room ID created above>
RoomSecret = <the 64-character room secret created above>
\`\`\`

**The URL is an example, not an existing service.** Each installation
automatically generates its own \`RecorderId\` (not an Orbus login ID);
never copy that field between PCs.

Both PCs must be able to reach the HTTPS relay. The relay does not
discover Orbus party members yet: **same private room = permission to
exchange recordings**. Only invite people you trust.

## How gameplay now works when connected

1. Both enable the regular five-minute rolling buffer and turn on PartySync.
2. Horizon presses **Ctrl+Shift+C** or **SAVE LAST**.
3. Their RC also posts a **save-last request** to the private party room.
   Jordan's running RC polls every five seconds and saves the matching
   recent buffer, *without* broadcasting another save request.
4. RC builds its existing \`.party.zip\` on disk and sends it over HTTPS
   in the background. This ZIP is **internal transport**, not something
   a player manually transfers.
5. The other recorder downloads it into its private PartyImports folder.
   The new **F2 → PARTY → OPEN SYNCED REPLAY** control selects the latest
   received clip and uses the existing UTC-aligned combined timeline.
6. When either recorder reconnects, RC retries up to twelve recent saved
   packages and fetches missing partner packages. Saved server-side files
   remain available after a participant leaves.

The game is not modified: this is a separate HTTPS recorder-data channel.
There is no injection into native Orbus voice, player or gameplay packets.
Network and file work stays off the Unity update/rendering thread.

## Known limitations and next milestones

- **Opt-in and configured once.** The relay room and its HTTPS address
  must be created/configured. It is *not* automatically determined by
  the game's live Orbus party yet.
- **Only saved buffers sync.** The rolling RAM buffer itself is not
  continuously replicated. If someone quits *without any clip being
  saved*, their unsaved preceding footage is lost. Automatic periodic
  checkpoints / chunk upload with bounded storage is the next milestone.
- When the other client is offline, a remote save request cannot recover
  their old unsaved RAM. Already uploaded saved clips remain available.
- This syncs client-observed pose frames, nearby actor tracks and rig
  bones, **not** a complete reconstructed dungeon. It does not
  automatically recover scenery that nobody's client loaded.
- Clock offsets are still handled in the Party tab. Zone names don't
  prove two clients joined the same *instance*. Add verified instance/session
  IDs before enabling unrestricted scene merging.
- Room files are retained until the host removes them. Each room accepts
  up to forty archives of at most 100 MiB each to bound growth. No remote
  file deletion or privileged game administration endpoints are exposed.
- The TLS reverse proxy and public route are not created by this repo.
- The current downloader fetches at most three missing clips per poll.
- Full portable world asset reconstruction, voice/audio capture and
  synchronized continuous 30-minute sessions are separate projects.

## Research and asset-reconstruction direction

The IL2CPP/Ghidra mapping in the OrbusModding research repo covers the
remote-player update path, local pose captures, net entity spawn/despawn
and zone transitions. Use it to:
- Record authoritative-or-observed session/zone instance IDs without
  claiming two equal zone names imply the same world.
- Keep capture identities stable across both recorders (server entity GUID
  where verified, never a temporary Unity instance ID as a shared key).
- Capture head/hands/body and equipment/rig asset references, and
  use installed legitimate local assets to render them offline.
- Build separate time-bounded scene/terrain coverage maps for each
  recorder, then create a union with explicit missing segments.
- Respect CPU/VR latency budgets: send compressed chunks asynchronously;
  never rebuild meshes or scan all colliders every frame.

## Validation

Relay protocol and path-safety tests:

\`\`\`powershell
node --test .\party-sync-relay\test\*.test.mjs
\`\`\`

Recorder integration compile-test (no game DLLs):

\`\`\`powershell
dotnet run --project .\tests\ReplayBuffer.IntegrationCompileTest.csproj -c Release
\`\`\`

A full \`BetterMirror.dll\` build needs the installed game/BepInEx
IL2CPP interop references; use local \`release.ps1\` only when changes
have been reviewed and merged into main. Test with two consenting PCs
and an HTTPS relay before enabling it for normal recording.
