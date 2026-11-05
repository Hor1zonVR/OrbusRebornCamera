using System;
using System.IO;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Render-only preview of locally observed, nearby non-local actor timelines.
/// No NPC AI is run and no world objects are instantiated or moved.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private WorldActorTimeline? _worldActorTimeline;
    private WorldRigTimeline? _worldRigTimeline;
    private bool _showObservedActors = true;
    private bool _showObservedBones = true;

    private void LoadWorldActorSidecar(string posePath)
    {
        _worldActorTimeline = null;
        _worldRigTimeline = null;
        string rigPath = Path.ChangeExtension(posePath, ".rigs.bin");
        if (File.Exists(rigPath))
        {
            try
            {
                using var stream = new FileStream(rigPath,
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                var rig = WorldRigCodec.Read(stream);
                if (_replayClip != null &&
                    rig.ClipId == _replayClip.ClipId &&
                    rig.SessionId == _replayClip.SessionId)
                {
                    _worldRigTimeline = rig;
                    Plugin.ModLogger.LogInfo(
                        "[WorldRigs] Loaded " + rig.RigCount +
                        " real skin rigs, " + rig.SampleCount +
                        " bone-pose frames.");
                }
                else
                {
                    Plugin.ModLogger.LogWarning(
                        "[WorldRigs] Skipped mismatched session/clip IDs.");
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[WorldRigs] Could not load bone sidecar: " + ex.Message);
            }
        }
        string path = Path.ChangeExtension(posePath, ".actors.tsv");
        if (!File.Exists(path))
        {
            Plugin.ModLogger.LogInfo(
                "[WorldActors] No actor sidecar; previous pose clips still work.");
            return;
        }
        try
        {
            _worldActorTimeline = WorldActorTimeline.ReadFile(path);
            Plugin.ModLogger.LogInfo(
                "[WorldActors] Loaded " + _worldActorTimeline.ActorCount +
                " actor tracks, " + _worldActorTimeline.ObservationCount +
                " samples from " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[WorldActors] Actor preview unavailable: " + ex.Message);
        }
    }

    private void CycleActorDisplay()
    {
        if (!_showObservedActors)
        {
            _showObservedActors = true;
            _showObservedBones = false; // simple axes
        }
        else if (!_showObservedBones)
            _showObservedBones = true; // real captured bone links
        else
        {
            _showObservedActors = false;
            _showObservedBones = false;
        }
    }

    private string ActorDisplayLabel => !_showObservedActors ? "OFF" :
        _showObservedBones ? "BONES" : "AXES";

    private static Vector3 AnimatedBonePosition(WorldRigSample first,
        WorldRigSample? second, float blend, int index)
    {
        WorldRigBonePose a = first.Bones[index];
        Vector3 pos = new Vector3(a.X, a.Y, a.Z);
        if (second == null || blend <= 0f) return pos;
        WorldRigBonePose b = second.Bones[index];
        return Vector3.Lerp(pos,
            new Vector3(b.X, b.Y, b.Z), blend);
    }

    private void RenderObservedRigBones()
    {
        if (!_showObservedActors || !_showObservedBones ||
            _worldRigTimeline == null || _worldActorTimeline == null ||
            _replayClip == null) return;

        double when = _replayClip.FirstSeconds + _replayElapsed;
        // Only actors currently observed in the matching actor timeline.
        // This prevents frozen bones after a pet/NPC leaves the recording.
        foreach (WorldActorObservation actor in _worldActorTimeline.VisibleAt(when))
        {
            if (!_worldRigTimeline.TryAt(actor.ActorKey, when,
                out WorldRigDefinition? definition, out WorldRigSample? first,
                out WorldRigSample? second, out float blend) ||
                definition == null || first == null)
                continue;
            Color tint = actor.Category == "remote_player_candidate"
                ? new Color(.96f, .58f, 1f, 1f)
                : new Color(.43f, .9f, 1f, 1f);
            int count = Math.Min(definition.BoneCount, first.Bones.Length);
            for (int i = 0; i < count; i++)
            {
                int parent = definition.ParentIndices[i];
                if (parent < 0 || parent >= count) continue;
                Vector3 from = AnimatedBonePosition(first, second, blend, parent);
                Vector3 to = AnimatedBonePosition(first, second, blend, i);
                DrawGuideLine(from, to, tint);
            }
        }
    }

    private void RenderWorldActorMarkers()
    {
        if (!_showObservedActors || _replayClip == null ||
            _worldActorTimeline == null) return;

        var at = _worldActorTimeline.VisibleAt(
            _replayClip.FirstSeconds + _replayElapsed);

        foreach (WorldActorObservation actor in at)
        {
            Vector3 p = new Vector3(actor.X, actor.Y, actor.Z);
            float s = actor.Category == "remote_player_candidate"
                ? .30f : .23f;
            Color tint = actor.Category == "remote_player_candidate"
                ? new Color(.95f, .47f, 1f, 1f)
                : new Color(1f, .67f, .30f, 1f);

            DrawGuideLine(p - Vector3.right * s,
                p + Vector3.right * s, tint);
            DrawGuideLine(p - Vector3.up * s,
                p + Vector3.up * s, tint);
            DrawGuideLine(p - Vector3.forward * s,
                p + Vector3.forward * s, tint);

            Quaternion q = new Quaternion(actor.Qx, actor.Qy,
                actor.Qz, actor.Qw);
            DrawGuideLine(p,
                p + q * Vector3.forward * s * 1.8f, tint);
        }
    }
}
