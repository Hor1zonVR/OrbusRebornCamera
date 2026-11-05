using UnityEngine;

namespace OrbusBetterMirror;

public sealed partial class CameraDiagnostics
{
    // The Recording tab is bounded by StudioContentY+406. UI stays compact
    // so all controls remain reachable on a small/scale-adjusted window.
    private void DrawRecordingTab(float x, float y, float w)
    {
        StudioSection(x, y, w, "INSTANT REPLAY BUFFER");
        StudioFill(new Rect(x, y + 29f, w, 46f), StudioSurface);
        StudioBorder(new Rect(x, y + 29f, w, 46f), StudioStroke);
        StudioText(new Rect(x + 11f, y + 31f, w - 22f, 21f),
            ReplayBufferRecorder.State + "  /  " +
            ReplayBufferRecorder.FrameCount + " frames", _studioHeading);
        StudioText(new Rect(x + 11f, y + 51f, w - 22f, 20f),
            ReplayBufferRecorder.AvailableSeconds.ToString("0") + " / " +
            ReplayBufferRecorder.DurationSeconds + " secs  |  " +
            ReplayBufferRecorder.LastSaveStatus, _studioMuted);

        if (StudioButton(new Rect(x, y + 82f, w, 36f),
            "SAVE LAST " + (ReplayBufferRecorder.DurationSeconds / 60f)
                .ToString("0.#") + " MINUTES   Ctrl+Shift+C", false, true))
            ReplayBufferRecorder.SaveLastClip();

        if (StudioButton(new Rect(x, y + 123f, w, 34f),
            "Rolling buffer: " +
            (ReplayBufferRecorder.Enabled ? "ENABLED" : "DISABLED"),
            ReplayBufferRecorder.Enabled))
            ReplayBufferRecorder.SetEnabled(!ReplayBufferRecorder.Enabled);

        float third = (w - 18f) / 3f;
        int[] values = { 60, 180, 300 };
        string[] labels = { "1 MIN", "3 MIN", "5 MIN" };
        for (int i = 0; i < values.Length; i++)
        {
            if (StudioButton(new Rect(x + i * (third + 9f),
                    y + 164f, third, 33f), labels[i],
                ReplayBufferRecorder.DurationSeconds == values[i]))
                ReplayBufferRecorder.SetDurationSeconds(values[i]);
        }

        StudioSection(x, y + 205f, w, "POSE REPLAY VIEWER (DESKTOP)");
        string info = _replayClip == null ? _replayMessage :
            _replayClip.Count + " frames | " +
            _replayClip.AvatarAnchorSamples + " avatar poses | " +
            _replayClip.TeleportCount + " teleports";
        StudioText(new Rect(x + 2f, y + 226f, w - 4f, 20f),
            info, _studioMuted);

        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 250f, half, 35f),
            "Load latest saved clip", false, true))
            LoadLatestReplay();
        if (StudioButton(new Rect(x + half + 9f, y + 250f,
            half, 35f), "Focus recorded head", false))
            FocusReplayHead();

        if (StudioButton(new Rect(x, y + 291f, half, 35f),
            _replayPlaying ? "Pause replay" : "Play replay",
            _replayPlaying))
            ToggleReplayPlayback();
        if (StudioButton(new Rect(x + half + 9f, y + 291f,
            half, 35f), "Close replay", false))
            ClearReplay();

        if (StudioButton(new Rect(x, y + 329f, w, 23f),
            "REPLAY GHOST BODY: " + (_replayShowGhost ? "ON" : "OFF"),
            _replayShowGhost))
            _replayShowGhost = !_replayShowGhost;

        float length = _replayClip == null ? 1f :
            (float)System.Math.Max(.01d, _replayClip.Duration);
        float next = StudioSlider(8, x, y + 355f, w, "Replay time",
            (float)_replayElapsed, 0f, length,
            ReplayTime(_replayElapsed) + " / " +
            ReplayTime(_replayClip?.Duration ?? 0d));
        if (_replayClip != null &&
            UnityEngine.Mathf.Abs(next - (float)_replayElapsed) > .001f)
            SeekReplay(next);
    }
}
