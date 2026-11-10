using System;
using System.IO;

namespace OrbusBetterMirror;

/// <summary>
/// Two independently captured local pose clips in one UTC-aligned timeline.
/// No Unity dependency; validated by synthetic tests in CI.
///
/// Same zone names are necessary, but not sufficient, to prove the recordings
/// came from the SAME dungeon instance. This viewer never invents remote
/// network packets, mesh geometry, or frames after one recorder leaves.
/// </summary>
internal sealed class ReplayPartyTimeline
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;
    private static readonly long MinimumUtc =
        new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
    private static readonly long MaximumUtc =
        new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    internal ReplayClipData Host { get; }
    internal ReplayClipData Partner { get; }
    internal double PartnerClockOffsetSeconds { get; }
    internal long StartUtcTicks { get; }
    internal double Duration { get; }
    internal double HostStart { get; }
    internal double PartnerStart { get; }
    internal bool HasOverlap { get; }

    internal ReplayPartyTimeline(ReplayClipData host,
        ReplayClipData partner, double partnerClockOffsetSeconds = 0)
    {
        if (host == null || partner == null)
            throw new ArgumentNullException(host == null ? nameof(host) : nameof(partner));
        if (host.ClipId.Length > 0 && host.ClipId == partner.ClipId)
            throw new InvalidDataException("The partner clip is the same recording as yours.");
        if (double.IsNaN(partnerClockOffsetSeconds) ||
            double.IsInfinity(partnerClockOffsetSeconds) ||
            Math.Abs(partnerClockOffsetSeconds) > 120d)
            throw new InvalidDataException("Manual sync offset must be within +/-120 seconds.");

        long hostStart = ValidUtc(host);
        long partnerStart = ValidUtc(partner);
        long offset = (long)Math.Round(
            partnerClockOffsetSeconds * TicksPerSecond);
        partnerStart = checked(partnerStart + offset);

        // Use measured MONOTONIC clip duration, NOT wall-clock time jumps
        // between frames, to prevent clock correction / PC sleep from
        // inventing a long unrecorded segment.
        long hostEnd = checked(hostStart + (long)Math.Round(
            host.Duration * TicksPerSecond));
        long partnerEnd = checked(partnerStart + (long)Math.Round(
            partner.Duration * TicksPerSecond));

        long first = Math.Min(hostStart, partnerStart);
        long last = Math.Max(hostEnd, partnerEnd);
        if (last - first > 30L * 60L * TicksPerSecond)
            throw new InvalidDataException(
                "Clips span more than 30 minutes. Check the chosen partner clip and PC clocks.");

        Host = host;
        Partner = partner;
        PartnerClockOffsetSeconds = partnerClockOffsetSeconds;
        StartUtcTicks = first;
        HostStart = (hostStart - first) / (double)TicksPerSecond;
        PartnerStart = (partnerStart - first) / (double)TicksPerSecond;
        Duration = (last - first) / (double)TicksPerSecond;
        HasOverlap = Math.Max(hostStart, partnerStart) <=
            Math.Min(hostEnd, partnerEnd);
    }

    private static long ValidUtc(ReplayClipData clip)
    {
        long first = clip.Frames[0].UtcTicks;
        long last = clip.Frames[clip.Count - 1].UtcTicks;
        if (first < MinimumUtc || last > MaximumUtc ||
            last < first || clip.Duration < 0 || clip.Duration > 900)
            throw new InvalidDataException(
                "Party replay requires valid UTC timestamps and a <=15 minute clip.");
        return first;
    }

    internal double ClipElapsed(int participant, double elapsed) =>
        elapsed - (participant == 0 ? HostStart : PartnerStart);

    internal bool TryEvaluate(int participant, double elapsed,
        out ReplayClipFrame first, out ReplayClipFrame second,
        out float blend)
    {
        first = null!;
        second = null!;
        blend = 0f;
        ReplayClipData clip = participant == 0 ? Host : Partner;
        double local = ClipElapsed(participant, elapsed);
        // Critically, do NOT clamp to clip start/end: absence before joining
        // or after leaving remains absent when the other recorder carries on.
        if (local < -0.00001 || local > clip.Duration + 0.00001)
            return false;
        clip.Evaluate(Math.Max(0d, Math.Min(clip.Duration, local)),
            out first, out second, out blend);
        if (!first.Head.Valid) return false;

        // The original viewer can HOLD an old pose across a teleport or
        // long sample gap. In a multi-user merged scene that is misleading;
        // show a temporary absence instead of a frozen duplicate actor.
        if (second != first &&
            second.Transition != ReplayTransitionKind.None &&
            local > first.Seconds - clip.FirstSeconds + 0.05d &&
            local < second.Seconds - clip.FirstSeconds - 0.005d)
            return false;
        return true;
    }

    internal static bool IsKnownZone(string? zone) =>
        !string.IsNullOrWhiteSpace(zone) &&
        zone != "(unknown)" && zone != "(not recorded)";

    /// <summary>
    /// A mismatch is never forced together. Even two matching zone names
    /// might be different private instances; the UI makes that caveat clear.
    /// </summary>
    internal bool CanShowTogether(double elapsed)
    {
        if (!TryEvaluate(0, elapsed, out ReplayClipFrame a, out _, out _) ||
            !TryEvaluate(1, elapsed, out ReplayClipFrame b, out _, out _))
            return false;
        return IsKnownZone(a.Zone) &&
            string.Equals(a.Zone, b.Zone, StringComparison.Ordinal);
    }

    internal string CoverageAt(double elapsed)
    {
        bool hasHost = TryEvaluate(0, elapsed, out var a, out _, out _);
        bool hasPartner = TryEvaluate(1, elapsed, out var b, out _, out _);
        if (!hasHost && !hasPartner) return "No recorded poses at this time";
        if (hasHost && !hasPartner) return "Only your recording available";
        if (!hasHost) return "Only partner recording available";
        if (!IsKnownZone(a.Zone) || !string.Equals(a.Zone, b.Zone, StringComparison.Ordinal))
            return "Different/unknown zones: do not merge positions";
        return "Two poses aligned (same zone; instance unverified)";
    }
}
