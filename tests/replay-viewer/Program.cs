using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OrbusBetterMirror;

static class ReplayReaderTests
{
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
    }

    // Small synthetic clips. Do not store the user's actual game positions
    // or private recordings in a public test repository.
    static string Pose(bool valid, float x)
    {
        return string.Join("\t", new [] {
            valid ? "1" : "0", x.ToString(CultureInfo.InvariantCulture),
            "0", "0", "0", "0", "0", "1"
        });
    }

    static string Clip(bool v2)
    {
        var s = new StringBuilder();
        s.AppendLine("# RebornCamera rolling pose clip v" + (v2 ? "2" : "1"));
        s.AppendLine("# clip_id=synthetic");
        s.AppendLine("# session_id=mock-session");
        string header = "seconds\tutc_ticks\tscene\thead_valid\thead_x\thead_y\thead_z\thead_qx\thead_qy\thead_qz\thead_qw" +
            "\tleft_valid\tleft_x\tleft_y\tleft_z\tleft_qx\tleft_qy\tleft_qz\tleft_qw" +
            "\tright_valid\tright_x\tright_y\tright_z\tright_qx\tright_qy\tright_qz\tright_qw";
        if (v2) header += "\tzone\ttransition";
        s.AppendLine(header);
        void Add(string seconds, float x, string zone, string transition, bool hand=true)
        {
            s.Append(seconds).Append("\t42\tBootstrapWeathermaker\t")
                .Append(Pose(true, x)).Append('\t')
                .Append(Pose(hand, x)).Append('\t')
                .Append(Pose(hand, x));
            if (v2) s.Append('\t').Append(zone).Append('\t').Append(transition);
            s.AppendLine();
        }
        Add("0", 0, "zone-a", "None");
        Add("0.033", 0.1f, "zone-a", "None");
        Add("0.2", 212f, "zone-a", "Teleport");
        Add("0.3", 212.1f, "zone-a", "None", false);
        Add("1.2", 212.2f, "zone-a", "Gap");
        Add("1.233", 212.3f, "zone-b", "ZoneChange");
        return s.ToString();
    }

    static void Test(bool v2)
    {
        ReplayClipData data = ReplayClipReader.Read(
            new StringReader(Clip(v2)), "mock.tsv");
        Check(data.Count == 6, "read frames " + v2);
        Check(data.Duration == 1.233d, "duration");
        Check(data.TeleportCount == 1, "teleport detection");
        Check(data.GapCount == 1, "gap detection");
        Check(data.ZoneChangeCount == (v2 ? 1 : 0), "zone metadata");
        Check(data.SessionId == "mock-session", "session metadata");
        data.Evaluate(.1, out var a, out var b, out float blend);
        Check(Math.Abs(a.Seconds - .033) < .0001 &&
            Math.Abs(b.Seconds - .2) < .0001 && blend == 0f,
            "hold prior position before teleport");
        data.Evaluate(.2, out a, out b, out blend);
        Check(Math.Abs(a.Head.X - 212f) < .001f,
            "snap at teleport boundary");
        data.Evaluate(.5, out a, out b, out blend);
        Check(blend == 0f && Math.Abs(a.Head.X - 212.1f) < .001f,
            "hold during recording gap");
        data.Evaluate(data.Duration + 100, out a, out b, out blend);
        Check(Math.Abs(a.Seconds - 1.233) < .0001, "seek clamp end");
        data.Evaluate(-10, out a, out b, out blend);
        Check(a.Seconds == 0d, "seek clamp start");
        Check(!data.Frames[3].Left.Valid, "invalid hand preserved");
    }

    static void Main()
    {
        Test(false);
        Test(true);
        bool refused = false;
        try
        {
            ReplayClipReader.Read(new StringReader("bad\tcolumns\n"),
                "invalid.tsv");
        }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "refuse invalid file");
        Console.WriteLine("ReplayClipReader tests passed (v1 + v2, jump, gap, zone, seek).");
    }
}
