using System;
using UnityEngine;

#if ORBUS_UI_AVAILABLE
using UnityEngine.UI;
#endif

namespace OrbusBetterMirror;

/// <summary>
/// Orbus-inspired cosmetic layer for the desktop Creator Studio.
/// Pure IMGUI drawing; no bundled/copyrighted fonts, game asset edits,
/// VR camera changes or dependencies on specific UI prefab names.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private Font? _studioGameFont;
    private float _nextStudioFontProbe;
    private bool _studioFontInfoLogged;

    /// <summary>
    /// Reuse an ALREADY LOADED Font that a genuine Unity TextMesh in an
    /// active Orbus scene is using. This is an asset reference, not a copy
    /// or redistribution. Orbus can use atlas/SuperTextMesh fonts that IMGUI
    /// cannot reuse; in that case we leave the readable Unity default.
    /// </summary>
    private void ProbeOrbusUiFont()
    {
        if (!Plugin.UseOrbusUiFont.Value || _studioGameFont != null ||
            Time.unscaledTime < _nextStudioFontProbe)
            return;

        _nextStudioFontProbe = Time.unscaledTime + 8f;
        try
        {
            Font? best = null;
            int bestScore = 0;
            int inspected = 0;

#if ORBUS_UI_AVAILABLE
            // Prefer fonts on actual Orbus canvas UI over miscellaneous
            // floating world labels, whenever legacy Unity UI exists.
            foreach (Text uiText in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (uiText == null || ++inspected > 1200) break;
                GameObject go = uiText.gameObject;
                if (go == null || !go.scene.IsValid() ||
                    !go.scene.isLoaded) continue;
                Font? candidate = uiText.font;
                if (candidate == null || string.IsNullOrEmpty(candidate.name))
                    continue;
                string name = (go.name ?? "").ToLowerInvariant();
                string parent = go.transform.parent != null ?
                    (go.transform.parent.name ?? "").ToLowerInvariant() : "";
                string context = name + " " + parent;
                int score = 18;
                if (context.Contains("menu") ||
                    context.Contains("journal") ||
                    context.Contains("quest") ||
                    context.Contains("title"))
                    score += 10;
                if (go.activeInHierarchy) score += 6;
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
#endif

            foreach (TextMesh mesh in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (mesh == null || ++inspected > 2000) break;
                GameObject go = mesh.gameObject;
                if (go == null || !go.scene.IsValid() ||
                    !go.scene.isLoaded) continue;

                Font? candidate = mesh.font;
                if (candidate == null) continue;
                string name = candidate.name ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                // Only sample fonts genuinely used by in-scene text. Prefer
                // menu, journal and story/headline text to generic 3D labels.
                string label = (go.name ?? "").ToLowerInvariant();
                string parent = go.transform.parent != null ?
                    (go.transform.parent.name ?? "").ToLowerInvariant() : "";
                string context = label + " " + parent;
                int score = 1;
                if (context.Contains("menu") || context.Contains("journal") ||
                    context.Contains("quest") || context.Contains("ui") ||
                    context.Contains("title") || context.Contains("book"))
                    score += 10;
                if (context.Contains("button") ||
                    context.Contains("character") ||
                    context.Contains("interface")) score += 6;
                if (go.activeInHierarchy) score += 3;
                string lowName = name.ToLowerInvariant();
                if (lowName.Contains("orbus") || lowName.Contains("fantasy") ||
                    lowName.Contains("serif") || lowName.Contains("book"))
                    score += 4;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
            {
                _studioGameFont = best;
                if (_studioTitle != null) _studioTitle.font = best;
                if (_studioHeading != null) _studioHeading.font = best;
                if (_studioTabActive != null) _studioTabActive.font = best;
                if (_studioTabInactive != null) _studioTabInactive.font = best;
                Plugin.ModLogger.LogInfo(
                    "[CreatorStudio] Using font from Orbus scene text '" +
                    best.name + "' for title, headings and tabs.");
            }
            else if (!_studioFontInfoLogged)
            {
                _studioFontInfoLogged = true;
                Plugin.ModLogger.LogInfo(
                    "[CreatorStudio] No compatible scene Unity font found yet. " +
                    "Keeping readable Unity font and retrying as Orbus loads.");
            }
        }
        catch (Exception ex)
        {
            if (!_studioFontInfoLogged)
            {
                _studioFontInfoLogged = true;
                Plugin.ModLogger.LogWarning(
                    "[CreatorStudio] Scene font lookup unavailable: " +
                    ex.GetType().Name + ". Using readable fallback.");
            }
        }
    }

    private static void StudioBorder(Rect r, Color edge, float width = 1f)
    {
        StudioFill(new Rect(r.x, r.y, r.width, width), edge);
        StudioFill(new Rect(r.x, r.yMax - width, r.width, width), edge);
        StudioFill(new Rect(r.x, r.y, width, r.height), edge);
        StudioFill(new Rect(r.xMax - width, r.y, width, r.height), edge);
    }

    private static void StudioCornerLines(Rect r)
    {
        const float edge = 13f;
        const float inset = 7f;
        Color soft = StudioBrassDim;
        // Short inlaid brass corner bars: an Orbus-like ornamental frame
        // without wasting space or obstructing any text/controls.
        StudioFill(new Rect(r.x + inset, r.y + inset, edge, 1f), soft);
        StudioFill(new Rect(r.x + inset, r.y + inset, 1f, edge), soft);
        StudioFill(new Rect(r.xMax - inset - edge, r.y + inset,
            edge, 1f), soft);
        StudioFill(new Rect(r.xMax - inset - 1f, r.y + inset,
            1f, edge), soft);
        StudioFill(new Rect(r.x + inset, r.yMax - inset - 1f,
            edge, 1f), soft);
        StudioFill(new Rect(r.x + inset, r.yMax - inset - edge,
            1f, edge), soft);
        StudioFill(new Rect(r.xMax - inset - edge,
            r.yMax - inset - 1f, edge, 1f), soft);
        StudioFill(new Rect(r.xMax - inset - 1f,
            r.yMax - inset - edge, 1f, edge), soft);
    }

    private static void StudioSeal(float x, float y)
    {
        // Stylized square rune/seal, constructed from flat Unity GUI quads.
        // No dependency on unusual characters, textures, or font glyphs.
        StudioFill(new Rect(x + 1f, y + 1f, 29f, 29f), StudioBrassDim);
        StudioFill(new Rect(x + 3f, y + 3f, 25f, 25f), StudioTop);
        StudioFill(new Rect(x + 8f, y + 8f, 15f, 15f), StudioGold);
        StudioFill(new Rect(x + 10f, y + 10f, 11f, 11f), StudioTop);
        StudioFill(new Rect(x + 14f, y + 5f, 3f, 21f), StudioGold);
        StudioFill(new Rect(x + 5f, y + 14f, 21f, 3f), StudioGold);
        StudioFill(new Rect(x + 13f, y + 13f, 5f, 5f), StudioJade);
    }

    private static void StudioDivider(float x, float y, float width)
    {
        StudioFill(new Rect(x, y, width, 1f), StudioStroke);
        StudioFill(new Rect(x, y, 33f, 1f), StudioBrassDim);
        StudioFill(new Rect(x + width - 33f, y, 33f, 1f),
            StudioBrassDim);
    }

    private void StudioSection(float x, float y, float width, string text)
    {
        StudioFill(new Rect(x, y + 7f, 3f, 12f), StudioGold);
        StudioText(new Rect(x + 13f, y, width - 13f, 27f),
            text, _studioHeading);
    }

    private void StudioFrame(float x, float y)
    {
        Rect outer = new Rect(x, y, StudioWidth, StudioHeight);
        StudioFill(new Rect(x - 3f, y - 3f,
            StudioWidth + 6f, StudioHeight + 6f), StudioShadow);
        StudioFill(outer, StudioBack);
        StudioBorder(outer, StudioBrassDim);
        StudioFill(new Rect(x + 2f, y + 2f,
            StudioWidth - 4f, StudioHeight - 4f), StudioStone);
        StudioFill(new Rect(x + 4f, y + 4f,
            StudioWidth - 8f, StudioHeight - 8f), StudioBack);
        StudioCornerLines(outer);
        StudioFill(new Rect(x + 17f, y + 1f, StudioWidth - 34f, 2f),
            StudioBrassDim);
    }

    private bool StudioTab(Rect rect, string text, bool active)
    {
        Vector2 mouse = StudioMouse(_panelScale);
        bool hover = rect.Contains(mouse);
        if (active || hover)
            StudioFill(rect, active ? StudioTabSurface :
                StudioSurfaceHover);
        else
            StudioFill(rect, StudioSurface);

        // The active tab gets an engraved brass underline, not a giant
        // mint rectangle. Decorative status never obscures its label.
        StudioFill(new Rect(rect.x + 1f, rect.y + 1f,
            rect.width - 2f, 1f),
            hover || active ? StudioBrassDim : StudioStroke);
        if (active)
            StudioFill(new Rect(rect.x + 13f,
                rect.yMax - 3f, rect.width - 26f, 2f), StudioGold);
        bool click = GUI.Button(rect, "", GUIStyle.none);
        StudioText(rect, text, active ?
            _studioTabActive : _studioTabInactive);
        return click;
    }

    private void StudioSequence(float x, float y, float width)
    {
        const float height = 56f;
        Rect area = new Rect(x, y, width, height);
        StudioFill(area, StudioSurface);
        StudioBorder(area, StudioStroke);
        StudioFill(new Rect(x, y, 3f, height), StudioBrassDim);
        StudioText(new Rect(x + 13f, y + 4f,
            width - 26f, 22f),
            "SHOT SEQUENCE  /  VISUAL OVERVIEW", _studioMuted);

        float left = x + 17f;
        float right = x + width - 17f;
        float baseline = y + 40f;
        StudioFill(new Rect(left, baseline - 1f, right - left, 2f),
            StudioStroke);

        if (_keyframes.Count == 0)
        {
            StudioText(new Rect(x + 13f, y + 27f,
                    width - 26f, 21f),
                "Add a shot to start your camera sequence",
                _studioMuted);
            return;
        }

        int count = _keyframes.Count;
        for (int i = 0; i < count; i++)
        {
            float fraction = count == 1 ?
                0f : i / (float)(count - 1);
            float center = Mathf.Lerp(left, right, fraction);
            float size = count > 45 ? 3f : count > 22 ? 4f : 6f;
            if (i == 0 || i == count - 1) size += 2f;
            StudioFill(new Rect(center - size / 2f,
                baseline - size / 2f, size, size),
                i == 0 ? StudioJade :
                i == count - 1 ? StudioGold : StudioBright);
        }

        if (_playingKeyframes)
        {
            float progress = Mathf.Clamp01(_pathElapsed /
                Mathf.Max(2f, Plugin.KeyframeDuration.Value));
            float playhead = Mathf.Lerp(left, right, progress);
            StudioFill(new Rect(playhead - 1f,
                baseline - 13f, 2f, 26f), StudioGold);
        }
    }
}
