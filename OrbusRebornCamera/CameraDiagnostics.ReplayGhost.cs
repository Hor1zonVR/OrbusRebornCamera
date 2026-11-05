using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Replay-only humanoid guide drawn from recorded head, hands and optional
/// avatar root. This is NOT a cloned Orbus model, bone animation or mesh.
/// It draws solely in RC's own GL pass on the desktop recording camera.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private bool _replayShowGhost = true;

    private static void GhostLine(Vector3 a, Vector3 b, Color tint) =>
        DrawGuideLine(a, b, tint);

    private static void DrawGhostJoint(Vector3 position, float r, Color tint)
    {
        GhostLine(position - Vector3.right * r,
            position + Vector3.right * r, tint);
        GhostLine(position - Vector3.up * r,
            position + Vector3.up * r, tint);
        GhostLine(position - Vector3.forward * r,
            position + Vector3.forward * r, tint);
    }

    private void DrawReplayHumanoid()
    {
        if (!_replayShowGhost || _replayClip == null) return;

        ReplayPoseValue head = EvaluatedPose(0);
        if (!head.Valid) return;
        ReplayPoseValue left = EvaluatedPose(1);
        ReplayPoseValue right = EvaluatedPose(2);
        ReplayPoseValue avatar = EvaluatedPose(3);

        Vector3 h = ReplayPosition(head);
        // v1/v2 recordings contain no avatar root. The fallback remains
        // explicitly a *guess*, never presented as the tracked model.
        Vector3 hip = avatar.Valid
            ? ReplayPosition(avatar) : h + Vector3.down * .85f;
        float bodyHeight = h.y - hip.y;
        if (bodyHeight < .35f || bodyHeight > 2.2f)
        {
            hip = h + Vector3.down * .85f;
            bodyHeight = .85f;
        }

        Vector3 facing = ReplayRotation(avatar.Valid ? avatar : head) *
            Vector3.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < .0001f)
            facing = ReplayRotation(head) * Vector3.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < .0001f) facing = Vector3.forward;
        facing.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;

        Vector3 neck = h + Vector3.down * .17f;
        Vector3 chest = hip + Vector3.up * (bodyHeight * .7f);
        Vector3 leftShoulder = chest - side * .22f;
        Vector3 rightShoulder = chest + side * .22f;
        Vector3 leftHip = hip - side * .14f;
        Vector3 rightHip = hip + side * .14f;
        Vector3 leftKnee = leftHip + Vector3.down * .45f;
        Vector3 rightKnee = rightHip + Vector3.down * .45f;
        Vector3 leftFoot = leftHip + Vector3.down * .88f +
            facing * .07f;
        Vector3 rightFoot = rightHip + Vector3.down * .88f +
            facing * .07f;

        Color body = avatar.Valid
            ? new Color(.36f, .89f, 1f, .95f)
            : new Color(.6f, .7f, .78f, .72f);
        Color joints = new Color(.96f, .89f, .66f, .95f);
        Color hands = new Color(.48f, 1f, .74f, 1f);

        // Stable body and legs.
        GhostLine(neck, chest, body);
        GhostLine(chest, hip, body);
        GhostLine(leftShoulder, rightShoulder, body);
        GhostLine(leftShoulder, leftHip, body);
        GhostLine(rightShoulder, rightHip, body);
        GhostLine(leftHip, rightHip, body);
        GhostLine(leftHip, leftKnee, body);
        GhostLine(leftKnee, leftFoot, body);
        GhostLine(rightHip, rightKnee, body);
        GhostLine(rightKnee, rightFoot, body);

        // The wrist/controller poses represent hand positions, not elbows.
        // Approximate an elbow to make the guide recognisably humanoid.
        if (left.Valid)
        {
            Vector3 hand = ReplayPosition(left);
            Vector3 elbow = Vector3.Lerp(leftShoulder, hand, .55f) +
                Vector3.down * .11f;
            GhostLine(leftShoulder, elbow, body);
            GhostLine(elbow, hand, hands);
            DrawGhostJoint(hand, .075f, hands);
        }
        if (right.Valid)
        {
            Vector3 hand = ReplayPosition(right);
            Vector3 elbow = Vector3.Lerp(rightShoulder, hand, .55f) +
                Vector3.down * .11f;
            GhostLine(rightShoulder, elbow, body);
            GhostLine(elbow, hand, hands);
            DrawGhostJoint(hand, .075f, hands);
        }

        // Stylised headset cross + face-forward line, not actual mesh.
        DrawGhostJoint(h, .16f, joints);
        GhostLine(h, h + ReplayRotation(head) * Vector3.forward * .25f,
            joints);
        DrawGhostJoint(hip, .09f, body);
    }
}
