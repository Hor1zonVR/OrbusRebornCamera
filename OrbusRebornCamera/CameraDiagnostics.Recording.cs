using UnityEngine;

namespace OrbusBetterMirror;

public sealed partial class CameraDiagnostics
{
    // Fourth Creator Studio tab. Uses the existing Orbus-themed panel
    // styles and layout; the in-world headset UI remains untouched.
    private void DrawRecordingTab(float x, float y, float w)
    {
        StudioSection(x, y, w, "INSTANT REPLAY BUFFER");

        StudioFill(new Rect(x, y + 31f, w, 68f), StudioSurface);
        StudioBorder(new Rect(x, y + 31f, w, 68f), StudioStroke);
        StudioText(new Rect(x + 13f, y + 37f, w - 26f, 23f),
            ReplayBufferRecorder.State, _studioHeading);

        string available = ReplayBufferRecorder.AvailableSeconds.ToString("0") +
            " / " + ReplayBufferRecorder.DurationSeconds +
            " seconds  |  " + ReplayBufferRecorder.FrameCount + " pose frames";
        StudioText(new Rect(x + 13f, y + 66f, w - 26f, 24f),
            available, _studioMuted);

        if (StudioButton(new Rect(x, y + 110f, w, 43f),
            "SAVE LAST " + (ReplayBufferRecorder.DurationSeconds / 60f)
                .ToString("0.#") + " MINUTES   Ctrl+Shift+C",
            false, true))
        {
            ReplayBufferRecorder.SaveLastClip();
        }

        if (StudioButton(new Rect(x, y + 162f, w, 39f),
            "Rolling buffer: " +
                (ReplayBufferRecorder.Enabled ? "ENABLED" : "DISABLED"),
            ReplayBufferRecorder.Enabled))
        {
            ReplayBufferRecorder.SetEnabled(!ReplayBufferRecorder.Enabled);
        }

        StudioSection(x, y + 210f, w, "BUFFER DURATION");
        float third = (w - 18f) / 3f;
        int[] values = { 60, 180, 300 };
        string[] labels = { "1 MIN", "3 MIN", "5 MIN" };
        for (int i = 0; i < values.Length; i++)
        {
            if (StudioButton(new Rect(x + i * (third + 9f),
                    y + 245f, third, 36f), labels[i],
                ReplayBufferRecorder.DurationSeconds == values[i]))
            {
                ReplayBufferRecorder.SetDurationSeconds(values[i]);
            }
        }

        StudioFill(new Rect(x, y + 300f, w, 102f), StudioSurface);
        StudioBorder(new Rect(x, y + 300f, w, 102f), StudioStroke);
        StudioText(new Rect(x + 12f, y + 306f, w - 24f, 22f),
            "LATEST CLIP STATUS", _studioHeading);
        StudioText(new Rect(x + 12f, y + 335f, w - 24f, 25f),
            ReplayBufferRecorder.LastSaveStatus, _studioMuted);
        StudioText(new Rect(x + 12f, y + 372f, w - 24f, 20f),
            "POSE DATA ONLY  /  NO WORLD REPLAY YET", _studioMuted);
    }
}
