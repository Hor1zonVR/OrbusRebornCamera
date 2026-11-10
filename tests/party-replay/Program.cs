using System;
using System.IO;
using System.IO.Compression;
using OrbusBetterMirror;

static class PartyReplayTests
{
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
    }

    private static ReplayClipData Clip(string id, long start,
        double duration, float x, string zone = "Zone-A")
    {
        ReplayClipFrame Frame(double seconds) => new()
        {
            Seconds = seconds,
            UtcTicks = start + (long)(seconds * TimeSpan.TicksPerSecond),
            Zone = zone,
            Scene = "SyntheticTestWorld",
            Head = new ReplayPoseValue
            {
                Valid = true, X = x + (float)seconds,
                Y = 1.6f, Z = 0f, Qw = 1f
            }
        };
        return new ReplayClipData(new [] { Frame(0), Frame(duration) },
            id, "synthetic-session-" + id, id + ".tsv");
    }

    private static void TestTimeAlignment()
    {
        long start = new DateTime(2026, 11, 10, 14, 0, 0,
            DateTimeKind.Utc).Ticks;
        var me = Clip("me", start, 20, 0);
        var jordan = Clip("jordan", start + 10 * TimeSpan.TicksPerSecond,
            20, 100);
        var session = new ReplayPartyTimeline(me, jordan);
        Check(session.HasOverlap, "clips overlap");
        Check(Math.Abs(session.Duration - 30) < .0001, "union includes late partner");
        Check(session.TryEvaluate(0, 5, out _, out _, out _), "host exists early");
        Check(!session.TryEvaluate(1, 5, out _, out _, out _),
            "partner not falsely rendered before starting");
        Check(session.TryEvaluate(1, 15, out _, out _, out _),
            "partner exists at shared moment");
        Check(session.CanShowTogether(15), "same-zone overlap");
        Check(!session.TryEvaluate(0, 25, out _, out _, out _),
            "host disappears after leaving");
        Check(session.TryEvaluate(1, 25, out _, out _, out _),
            "partner continues after host leaves");
        Check(session.CoverageAt(25).Contains("Only partner"),
            "coverage identifies partner-only tail");

        var corrected = new ReplayPartyTimeline(me, jordan, -4);
        Check(Math.Abs(corrected.PartnerStart - 6) < .01,
            "clock offset shifts partner recording only");

        var diffZone = new ReplayPartyTimeline(me,
            Clip("other-zone", start, 20, 100, "Zone-B"));
        Check(!diffZone.CanShowTogether(10),
            "never merge two different scene zones");

        var unknown = new ReplayPartyTimeline(me,
            Clip("unknown", start, 20, 100, "(unknown)"));
        Check(!unknown.CanShowTogether(10),
            "unknown zone cannot be trusted for world merge");

        bool duplicateRejected = false;
        try { _ = new ReplayPartyTimeline(me, me); }
        catch (InvalidDataException) { duplicateRejected = true; }
        Check(duplicateRejected, "reject same clip twice");

        bool badUtcRejected = false;
        try { _ = new ReplayPartyTimeline(me, Clip("bad", 42, 20, 0)); }
        catch (InvalidDataException) { badUtcRejected = true; }
        Check(badUtcRejected, "reject legacy clip without UTC");
    }

    private static void TestPackageTransfer()
    {
        string temp = Path.Combine(Path.GetTempPath(),
            "rc-party-synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string filename = "RC_Clip_20261110_140000_12345678";
            string own = Path.Combine(temp, filename + ".tsv");
            File.WriteAllText(own, "SYNTHETIC pose TSV data");
            File.WriteAllText(Path.Combine(temp,
                filename + ".actors.tsv"), "SYNTHETIC actor samples");
            File.WriteAllText(Path.Combine(temp,
                filename + ".avatar.txt"), "SYNTHETIC metadata");
            string zip = ReplayPartyArchive.Create(own);
            Check(File.Exists(zip), "clip share ZIP generated");
            using (var stream = ZipFile.OpenRead(zip))
                Check(stream.Entries.Count == 3,
                    "main clip and two optional sidecars packaged");

            string imported = ReplayPartyArchive.Import(zip,
                Path.Combine(temp, "Imported"));
            Check(File.Exists(imported), "pose extracted");
            Check(File.Exists(Path.ChangeExtension(imported,
                ".actors.tsv")), "actor sidecar extracted");
            Check(File.ReadAllText(imported) == "SYNTHETIC pose TSV data",
                "imported data matches source");
            Check(ReplayPartyArchive.Import(zip, Path.Combine(temp,
                "Imported")) == imported, "reimport non destructive");

            string malicious = Path.Combine(temp, "malicious.party.zip");
            using (var z = ZipFile.Open(malicious, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(
                    z.CreateEntry("../RC_Clip_bad.tsv").Open());
                writer.Write("bad");
            }
            bool refused = false;
            try { ReplayPartyArchive.Import(malicious,
                Path.Combine(temp, "Imported")); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "zip traversal refused");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    static void Main()
    {
        TestTimeAlignment();
        TestPackageTransfer();
        Console.WriteLine("Party replay tests passed: UTC union, late/early coverage, zone isolation, offset, ZIP transfer and traversal rejection.");
    }
}
