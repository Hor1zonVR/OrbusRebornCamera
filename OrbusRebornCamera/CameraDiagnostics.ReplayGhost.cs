using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Debug-only visualization of transforms actually captured from the local
/// PlayerRigv4 hierarchy. No invented torso, legs, knees or mesh geometry.
/// v1-v3 clips simply show the existing XR head/controller markers.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private bool _replayShowRigTracks = true;

    private static void DrawRigTrack(ReplayPoseValue pose, float radius,
        Color colour)
    {
        if (!pose.Valid) return;
        Vector3 p = ReplayPosition(pose);
        DrawGuideLine(p - Vector3.right * radius,
            p + Vector3.right * radius, colour);
        DrawGuideLine(p - Vector3.up * radius,
            p + Vector3.up * radius, colour);
        DrawGuideLine(p - Vector3.forward * radius,
            p + Vector3.forward * radius, colour);
        DrawGuideLine(p,
            p + ReplayRotation(pose) * Vector3.forward * radius * 2f, colour);
    }

    private void DrawRecordedRigTracks()
    {
        if (!_replayShowRigTracks || _replayClip == null ||
            _replayClip.RigRootSamples == 0)
            return;

        ReplayPoseValue root = EvaluatedPose(4);
        ReplayPoseValue torso = EvaluatedPose(3);
        ReplayPoseValue visualHead = EvaluatedPose(5);
        ReplayPoseValue modelLeft = EvaluatedPose(6);
        ReplayPoseValue modelRight = EvaluatedPose(7);

        Color rootColour = new Color(.76f, .63f, 1f, 1f);
        Color bodyColour = new Color(.32f, .96f, .78f, 1f);
        Color headColour = new Color(.96f, .88f, .39f, 1f);
        Color handColour = new Color(.94f, .6f, .94f, 1f);

        DrawRigTrack(root, .11f, rootColour);
        DrawRigTrack(torso, .18f, bodyColour);
        DrawRigTrack(visualHead, .12f, headColour);
        DrawRigTrack(modelLeft, .09f, handColour);
        DrawRigTrack(modelRight, .09f, handColour);

        // Connections only between actually recorded scene transforms.
        // These are not animation bones or reconstructed character geometry.
        if (torso.Valid && visualHead.Valid)
            DrawGuideLine(ReplayPosition(torso),
                ReplayPosition(visualHead), bodyColour);
        if (torso.Valid && modelLeft.Valid)
            DrawGuideLine(ReplayPosition(torso),
                ReplayPosition(modelLeft), bodyColour);
        if (torso.Valid && modelRight.Valid)
            DrawGuideLine(ReplayPosition(torso),
                ReplayPosition(modelRight), bodyColour);
    }
}
