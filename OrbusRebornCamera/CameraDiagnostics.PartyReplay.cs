using System;
using System.IO;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// First multiplayer milestone: import a second RECORDER's clip and place
/// both local-player pose tracks on one bounded, UTC-aligned timeline.
/// No network sync / automatic file transfer / remote world reconstruction.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private ReplayPartyTimeline? _partyTimeline;
    private ReplayClipData? _partyPartner;
    private WorldActorTimeline? _partyActors;
    private WorldRigTimeline? _partyRigs;
    private string _partyStatus = "Auto Sync: saved clips arrive automatically when relay is configured.";
    private double _partyOffsetSeconds;
    private int _partyFocus; // 0 = own recording, 1 = imported partner
    private bool _partyFollow;

    private double ReplayLength =>
        _partyTimeline?.Duration ?? _replayClip?.Duration ?? 0d;

    private double LocalReplayElapsed =>
        _partyTimeline == null ? _replayElapsed :
        _partyTimeline.ClipElapsed(0, _replayElapsed);

    private bool PrimaryPartyRecordingAvailable =>
        _partyTimeline == null ||
        _partyTimeline.TryEvaluate(0, _replayElapsed, out _, out _, out _);

    private string PartyImportFolder => Path.Combine(
        BepInEx.Paths.ConfigPath, "OrbusRebornCamera", "PartyImports");

    private void LoadImportedPartyClip()
    {
        try
        {
            if (_replayClip == null) LoadLatestReplay(false);
            if (_replayClip == null)
            {
                _partyStatus = "First save/load your own clip on REPLAY.";
                return;
            }

            Directory.CreateDirectory(PartyImportFolder);
            string[] packages = Directory.GetFiles(
                PartyImportFolder, "*.party.zip", SearchOption.AllDirectories);
            string[] poses = Array.FindAll(
                Directory.GetFiles(PartyImportFolder, "RC_Clip_*.tsv", SearchOption.AllDirectories),
                file => !file.EndsWith(".actors.tsv",
                    StringComparison.OrdinalIgnoreCase));
            string[] files = new string[packages.Length + poses.Length];
            packages.CopyTo(files, 0);
            poses.CopyTo(files, packages.Length);
            if (files.Length == 0)
            {
                _partyStatus = "No synced partner clips yet. Check Party Sync connection.";
                Plugin.ModLogger.LogWarning(
                    "[PartyReplay] Waiting for partner archive in: " +
                    PartyImportFolder);
                return;
            }
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b)
                .CompareTo(File.GetLastWriteTimeUtc(a)));
            string import = files[0];
            string pose = import.EndsWith(".party.zip",
                StringComparison.OrdinalIgnoreCase)
                ? ReplayPartyArchive.Import(import, Path.Combine(
                    BepInEx.Paths.ConfigPath,
                    "OrbusRebornCamera", "Clips", "Imported"))
                : import;
            ReplayClipData partner = ReplayClipReader.ReadFile(pose);
            ReplayPartyTimeline timeline = new ReplayPartyTimeline(
                _replayClip, partner, _partyOffsetSeconds);
            WorldActorTimeline? importedActors = null;
            WorldRigTimeline? importedRigs = null;
            try
            {
                string actorsPath = Path.ChangeExtension(pose, ".actors.tsv");
                if (File.Exists(actorsPath))
                    importedActors = WorldActorTimeline.ReadFile(actorsPath);
                string rigsPath = Path.ChangeExtension(pose, ".rigs.bin");
                if (File.Exists(rigsPath))
                {
                    using Stream rigFile = File.OpenRead(rigsPath);
                    WorldRigTimeline rig = WorldRigCodec.Read(rigFile);
                    if (rig.ClipId == partner.ClipId &&
                        rig.SessionId == partner.SessionId)
                        importedRigs = rig;
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[PartyReplay] Optional partner actor/rig data unavailable: " +
                    ex.Message);
            }
            _partyActors = importedActors;
            _partyRigs = importedRigs;
            _partyPartner = partner;
            _partyTimeline = timeline;
            _partyFocus = 0;
            _partyFollow = false;
            // Start on the host's recorded time, not before the host arrived
            // if Jordan began his recording earlier.
            _replayElapsed = timeline.HostStart;
            _replayPlaying = false;
            _partyStatus = "Imported " + partner.Count +
                " frames / " + (importedActors?.ActorCount ?? 0) +
                " actor tracks";
            Plugin.ModLogger.LogInfo("[PartyReplay] Loaded pair: host=" +
                _replayClip.ClipId + ", partner=" + partner.ClipId +
                ", UTC span=" + ReplayTime(timeline.Duration) +
                ", clockOffset=" + _partyOffsetSeconds.ToString("0.0") +
                "s. Same zone does not prove same dungeon instance.");
            if (_offlineReplayActive)
            {
                BuildOfflineVisuals();
                FocusPartyParticipant(0);
            }
        }
        catch (Exception ex)
        {
            _partyStatus = "Import failed - check BepInEx log";
            Plugin.ModLogger.LogWarning("[PartyReplay] Import error: " + ex);
        }
    }

    private void ClearPartyReplay()
    {
        _partyTimeline = null;
        _partyPartner = null;
        _partyActors = null;
        _partyRigs = null;
        _partyFocus = 0;
        _partyFollow = false;
        _partyOffsetSeconds = 0;
        _partyStatus = "Partner cleared. Single-player replay is available.";
        _replayElapsed = 0;
        _replayPlaying = false;
    }

    private void SetPartnerClockOffset(double changed)
    {
        if (_partyTimeline == null || _replayClip == null ||
            _partyPartner == null) return;
        changed = Math.Round(Math.Max(-120d,
            Math.Min(120d, changed)), 1);
        if (Math.Abs(changed - _partyOffsetSeconds) < 0.05d) return;

        // Retain the real-world playback instant while the timeline origin
        // moves due to a clock correction. Do not implicitly warp player
        // poses or alter any saved recording coordinates.
        long priorTime = _partyTimeline.StartUtcTicks +
            (long)Math.Round(_replayElapsed * TimeSpan.TicksPerSecond);
        var updated = new ReplayPartyTimeline(
            _replayClip, _partyPartner, changed);
        _partyOffsetSeconds = changed;
        _partyTimeline = updated;
        _replayElapsed = Math.Max(0d, Math.Min(updated.Duration,
            (priorTime - updated.StartUtcTicks) /
            (double)TimeSpan.TicksPerSecond));
        _replayPlaying = false;
    }

    private ReplayPoseValue PartyPose(int participant, int channel)
    {
        if (_partyTimeline == null ||
            !_partyTimeline.TryEvaluate(participant, _replayElapsed,
                out ReplayClipFrame a, out ReplayClipFrame b,
                out float blend))
            return default;
        return InterpolateReplayPose(a, b, blend, channel);
    }

    private bool PartyPartnerVisible()
    {
        if (_partyTimeline == null ||
            !_partyTimeline.TryEvaluate(1, _replayElapsed,
                out _, out _, out _)) return false;
        // If both players have poses but their zones differ (or unknown),
        // never draw two spatially overlapping instances as one scene.
        if (_partyTimeline.TryEvaluate(0, _replayElapsed,
                out _, out _, out _))
            return _partyTimeline.CanShowTogether(_replayElapsed);
        return true; // Host left; partner's later recording continues alone.
    }

    private void RenderPartyMarkers()
    {
        if (_partyTimeline == null || !PartyPartnerVisible())
            return;

        // Distinct second player's actual recorded head and hand poses.
        // A full character mesh is NOT encoded in these archives.
        var blue = new Color(.95f, .51f, .96f, 1f);
        var left = new Color(1f, .76f, .43f, 1f);
        var right = new Color(.74f, .65f, 1f, 1f);
        ReplayPoseValue head = PartyPose(1, 0);
        ReplayPoseValue torso = PartyPose(1, 3);
        ReplayPoseValue lh = PartyPose(1, 1);
        ReplayPoseValue rh = PartyPose(1, 2);
        DrawReplayMarker(head, .3f, blue);
        DrawReplayMarker(lh, .15f, left);
        DrawReplayMarker(rh, .15f, right);
        if (torso.Valid)
        {
            DrawReplayMarker(torso, .19f, blue);
            if (head.Valid)
                DrawGuideLine(ReplayPosition(torso),
                    ReplayPosition(head), blue);
            if (lh.Valid)
                DrawGuideLine(ReplayPosition(torso),
                    ReplayPosition(lh), left);
            if (rh.Valid)
                DrawGuideLine(ReplayPosition(torso),
                    ReplayPosition(rh), right);
        }
    }

    private void RenderPartnerActorCoverage()
    {
        if (_partyTimeline == null || _partyPartner == null ||
            !_showObservedActors || _partyActors == null ||
            !PartyPartnerVisible()) return;

        if (!_partyTimeline.TryEvaluate(1, _replayElapsed,
            out ReplayClipFrame partnerFrame, out _, out _)) return;

        double partnerTime = _partyPartner.FirstSeconds +
            _partyTimeline.ClipElapsed(1, _replayElapsed);
        WorldActorObservation[] actors = _partyActors.VisibleAt(partnerTime);
        WorldActorObservation[] hostActors = Array.Empty<WorldActorObservation>();
        if (PrimaryPartyRecordingAvailable && _worldActorTimeline != null &&
            _replayClip != null)
            hostActors = _worldActorTimeline.VisibleAt(
                _replayClip.FirstSeconds + LocalReplayElapsed);

        foreach (WorldActorObservation actor in actors)
        {
            if (ReplayPartyTimeline.IsKnownZone(actor.Zone) &&
                !string.Equals(actor.Zone, partnerFrame.Zone,
                    StringComparison.Ordinal))
                continue;

            // The same NPC may be seen by both clients with DIFFERENT
            // locally generated Unity IDs. Prefer a spatial duplicate
            // check rather than pretending those IDs are globally stable.
            bool duplicate = false;
            foreach (WorldActorObservation own in hostActors)
            {
                float dx = own.X - actor.X, dy = own.Y - actor.Y,
                    dz = own.Z - actor.Z;
                if (own.Category == actor.Category &&
                    own.Prefab == actor.Prefab &&
                    dx * dx + dy * dy + dz * dz < 1.5f * 1.5f)
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate) continue;

            Vector3 p = new Vector3(actor.X, actor.Y, actor.Z);
            Color tint = new Color(.96f, .64f, .90f, 1f);
            float size = actor.Category == "remote_player_candidate"
                ? .3f : .22f;
            DrawGuideLine(p - Vector3.right * size,
                p + Vector3.right * size, tint);
            DrawGuideLine(p - Vector3.up * size,
                p + Vector3.up * size, tint);
            DrawGuideLine(p - Vector3.forward * size,
                p + Vector3.forward * size, tint);

            if (_showObservedBones && _partyRigs != null &&
                _partyRigs.TryAt(actor.ActorKey, partnerTime,
                    out WorldRigDefinition? def,
                    out WorldRigSample? a,
                    out WorldRigSample? b,
                    out float blend) && def != null && a != null)
            {
                int count = Math.Min(def.BoneCount, a.Bones.Length);
                for (int i = 0; i < count; i++)
                {
                    int parent = def.ParentIndices[i];
                    if (parent < 0 || parent >= count) continue;
                    DrawGuideLine(AnimatedBonePosition(a, b, blend, parent),
                        AnimatedBonePosition(a, b, blend, i), tint);
                }
            }
        }
    }

    private void FocusPartyParticipant(int participant)
    {
        if (_partyTimeline == null) return;
        ReplayPoseValue head = PartyPose(participant, 0);
        if (!head.Valid)
        {
            _partyStatus = "No pose for that player at this replay time.";
            return;
        }
        if (_mode != CameraMode.Drone && !EnterFreecam())
        {
            _partyStatus = "Start OFFLINE preview to use the replay camera.";
            return;
        }
        _partyFocus = participant;
        PlacePartyCamera(head, true);
        Plugin.ModLogger.LogInfo("[PartyReplay] Focused " +
            (participant == 0 ? "host" : "partner") +
            " recorded head. Camera only.");
    }

    private void PlacePartyCamera(ReplayPoseValue head, bool snap)
    {
        if (!head.Valid) return;
        Vector3 forward = ReplayRotation(head) * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 position = ReplayPosition(head);
        // Recorded subject remains centred. Settings match the live third
        // person camera, including adjustable distance and height.
        Vector3 target = position - forward * Mathf.Clamp(
            Plugin.ThirdDistance.Value, .5f, 16f) +
            Vector3.up * Mathf.Clamp(Plugin.ThirdHeight.Value, -2f, 5f);
        Quaternion look = Quaternion.LookRotation(
            (position + Vector3.down * .45f - target).normalized,
            Vector3.up);
        if (snap)
            _dronePosition = target;
        else
        {
            float speed = Mathf.Clamp(Plugin.Smoothing.Value, 0f, 30f);
            float t = speed < .01f ? 1f :
                1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            _dronePosition = Vector3.Lerp(_dronePosition, target, t);
        }
        Vector3 angles = look.eulerAngles;
        _droneYaw = angles.y;
        _dronePitch = Mathf.DeltaAngle(0f, angles.x);
        _droneRoll = 0f;
        _droneVelocity = Vector3.zero;
    }

    // Called by ApplyDronePose in both normal and detached offline replay.
    private void UpdatePartyFollowCamera()
    {
        if (!_partyFollow || _partyTimeline == null ||
            _mode != CameraMode.Drone || _playingKeyframes)
            return;
        if (_partyFocus == 1 && !PartyPartnerVisible())
            return;
        ReplayPoseValue head = PartyPose(_partyFocus, 0);
        if (head.Valid) PlacePartyCamera(head, false);
    }

    private void DrawPartyTab(float x, float y, float w)
    {
        StudioSection(x, y, w, "PARTY REPLAY - AUTOMATIC CLIP SYNC");
        StudioFill(new Rect(x, y + 28f, w, 58f), StudioSurface);
        StudioBorder(new Rect(x, y + 28f, w, 58f), StudioStroke);
        StudioText(new Rect(x + 10f, y + 32f, w - 20f, 24f),
            "Both enable the same private Party Sync room.",
            _studioBody);
        StudioText(new Rect(x + 10f, y + 56f, w - 20f, 21f),
            ReplayBufferRecorder.PartySyncStatus, _studioMuted);

        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 95f, half, 36f),
            "Load my latest clip", false))
            LoadLatestReplay(false);
        if (StudioButton(new Rect(x + half + 9f, y + 95f,
            half, 36f), "OPEN SYNCED REPLAY", false, true))
            LoadImportedPartyClip();

        StudioText(new Rect(x + 4f, y + 139f, w - 8f, 22f),
            _partyStatus, _studioMuted);
        string coverage = _partyTimeline == null
            ? "No imported partner clip"
            : _partyTimeline.CoverageAt(_replayElapsed);
        StudioText(new Rect(x + 4f, y + 165f, w - 8f, 22f),
            coverage, _studioBody);

        float offset = StudioSlider(10, x, y + 196f, w,
            "Partner clock correction", (float)_partyOffsetSeconds,
            -120f, 120f, _partyOffsetSeconds.ToString("+0.0;-0.0;0.0") +
                " sec");
        if (Math.Abs(offset - _partyOffsetSeconds) >= .05f)
            SetPartnerClockOffset(offset);

        if (StudioButton(new Rect(x, y + 246f, half, 33f),
            "Focus my replay", _partyFocus == 0))
            FocusPartyParticipant(0);
        if (StudioButton(new Rect(x + half + 9f, y + 246f,
            half, 33f), "Focus partner", _partyFocus == 1))
            FocusPartyParticipant(1);

        if (StudioButton(new Rect(x, y + 288f, half, 34f),
            _offlineReplayActive ? "End offline stage" :
                "Start offline stage", _offlineReplayActive))
        {
            if (_offlineReplayActive) StopOfflineReplay();
            else StartOfflineReplay();
        }
        if (StudioButton(new Rect(x + half + 9f, y + 288f,
            half, 34f),
            _partyFollow ? "Camera follow: ON" : "Camera follow: OFF",
            _partyFollow))
        {
            _partyFollow = !_partyFollow && _partyTimeline != null;
            if (_partyFollow) FocusPartyParticipant(_partyFocus);
        }

        if (StudioButton(new Rect(x, y + 327f, half, 31f),
            _replayPlaying ? "PAUSE" : "PLAY", _replayPlaying))
            ToggleReplayPlayback();
        if (StudioButton(new Rect(x + half + 9f, y + 327f,
            half, 31f), "Clear partner", false))
            ClearPartyReplay();

        float length = (float)Math.Max(0.01d, ReplayLength);
        float seek = StudioSlider(11, x, y + 362f, w,
            "Combined timeline", (float)_replayElapsed,
            0f, length, ReplayTime(_replayElapsed) +
                " / " + ReplayTime(ReplayLength));
        if (_replayClip != null &&
            Mathf.Abs(seek - (float)_replayElapsed) > .001f)
            SeekReplay(seek);
    }
}
