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
    private bool _showObservedActors = true;

    private void LoadWorldActorSidecar(string posePath)
    {
        _worldActorTimeline = null;
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
