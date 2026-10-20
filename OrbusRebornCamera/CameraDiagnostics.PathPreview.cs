using System;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Local, desktop-camera-only editing guides. Never creates physical scene
/// colliders or sends player/network information. Drawn only by the detached
/// recording camera while F2 editor is open, and never during playback.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private Material? _pathPreviewMaterial;
    private bool _pathPreviewFailureLogged;
    private bool _pathPreviewUnavailable;

    private bool EnsurePathPreviewMaterial()
    {
        if (_pathPreviewMaterial != null) return true;
        if (_pathPreviewUnavailable) return false;

        try
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                _pathPreviewUnavailable = true;
                Plugin.ModLogger.LogWarning(
                    "[PathPreview] No compatible world-line shader found.");
                return false;
            }

            _pathPreviewMaterial = new Material(shader);
            // Unity built-in Internal-Colored uses these property names.
            // Draw editing aids on top of the scene, without altering any
            // shared game material or real-world object.
            if (_pathPreviewMaterial.HasProperty("_SrcBlend"))
                _pathPreviewMaterial.SetInt("_SrcBlend", 5); // SrcAlpha
            if (_pathPreviewMaterial.HasProperty("_DstBlend"))
                _pathPreviewMaterial.SetInt("_DstBlend", 10); // OneMinusSrcAlpha
            if (_pathPreviewMaterial.HasProperty("_Cull"))
                _pathPreviewMaterial.SetInt("_Cull", 0); // Off
            if (_pathPreviewMaterial.HasProperty("_ZWrite"))
                _pathPreviewMaterial.SetInt("_ZWrite", 0);
            if (_pathPreviewMaterial.HasProperty("_ZTest"))
                _pathPreviewMaterial.SetInt("_ZTest", 8); // Always
            return true;
        }
        catch (Exception ex)
        {
            _pathPreviewUnavailable = true;
            Plugin.ModLogger.LogWarning(
                "[PathPreview] Could not prepare line material: " +
                ex.GetType().Name);
            return false;
        }
    }

    private static void DrawGuideLine(Vector3 from, Vector3 to, Color color)
    {
        GL.Color(color);
        GL.Vertex3(from.x, from.y, from.z);
        GL.Vertex3(to.x, to.y, to.z);
    }

    private void OnRenderObject()
    {
        if (!_panelVisible || _playingKeyframes ||
            !Plugin.ShowKeyframePath.Value ||
            !IsCameraActive || _desktop == null ||
            _keyframes.Count == 0 ||
            _keyframeScene != ActiveSceneName())
            return;

        try
        {
            // Camera.current is the Unity camera currently rendering this
            // callback. Never draw helpers in the headset/eye camera or any
            // other scene, even when using RebornCam from VR mode.
            if (Camera.current != _desktop ||
                !EnsurePathPreviewMaterial() ||
                _pathPreviewMaterial == null)
                return;

            if (!_pathPreviewMaterial.SetPass(0))
                return;

            GL.PushMatrix();
            try
            {
                GL.MultMatrix(Matrix4x4.identity);
                GL.Begin(GL.LINES);
                try
                {
                    Color pathColor = new Color(0.27f, 0.91f, 0.82f, .93f);
                    Color markerColor = new Color(.92f, .96f, 1f, .98f);
                    Color firstColor = new Color(.30f, .96f, .63f, 1f);
                    Color lastColor = new Color(1f, .65f, .37f, 1f);

                    for (int segment = 0;
                         segment < _keyframes.Count - 1; segment++)
                    {
                        Vector3 previous = _keyframes[segment].Position;
                        for (int step = 1; step <= 18; step++)
                        {
                            Vector3 current = EvaluateKeyframePosition(
                                segment, step / 18f);
                            DrawGuideLine(previous, current, pathColor);
                            previous = current;
                        }
                    }

                    for (int i = 0; i < _keyframes.Count; i++)
                    {
                        var frame = _keyframes[i];
                        Vector3 pos = frame.Position;
                        float distance = Vector3.Distance(
                            _desktop.transform.position, pos);
                        float radius = Mathf.Clamp(distance * .008f,
                            .09f, .8f);
                        Color tint = i == 0 ? firstColor :
                            i == _keyframes.Count - 1 ?
                                lastColor : markerColor;
                        DrawGuideLine(pos - Vector3.right * radius,
                            pos + Vector3.right * radius, tint);
                        DrawGuideLine(pos - Vector3.up * radius,
                            pos + Vector3.up * radius, tint);
                        DrawGuideLine(pos - Vector3.forward * radius,
                            pos + Vector3.forward * radius, tint);
                        Vector3 direction = frame.Rotation *
                            Vector3.forward;
                        DrawGuideLine(pos, pos + direction * radius * 2f,
                            tint);
                    }
                }
                finally { GL.End(); }
            }
            finally { GL.PopMatrix(); }
        }
        catch (Exception ex)
        {
            // An unsupported shader or GL wrapper must NEVER prevent the
            // cinematic camera or normal VR gameplay from rendering.
            if (!_pathPreviewFailureLogged)
            {
                _pathPreviewFailureLogged = true;
                Plugin.ModLogger.LogWarning(
                    "[PathPreview] Visual aid failed; disabling preview: " +
                    ex);
            }
            _pathPreviewUnavailable = true;
        }
    }

    private void DestroyPathPreviewMaterial()
    {
        if (_pathPreviewMaterial == null) return;
        try { UnityEngine.Object.Destroy(_pathPreviewMaterial); }
        catch { /* Scene/shutdown cleanup. */ }
        _pathPreviewMaterial = null;
    }
}
