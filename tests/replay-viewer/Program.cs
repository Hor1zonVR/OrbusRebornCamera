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
        // Grid is stationary inside one uninterrupted region. It changes
        // only when replay actually snaps to a teleport/new loaded zone.
        Check(Math.Abs(data.GridReferenceAt(.1).X) < .001f,
            "grid remains at original segment before teleport");
        Check(Math.Abs(data.GridReferenceAt(.2).X - 212f) < .001f,
            "grid relocates to recorded teleport destination");
        Check(Math.Abs(data.GridReferenceAt(.5).X - 212f) < .001f,
            "ordinary locomotion does not move grid anchor");
        Check(Math.Abs(data.GridReferenceAt(1.2).X - 212f) < .001f,
            "recording gap does not shift grid anchor");
        if (v2)
            Check(Math.Abs(data.GridReferenceAt(1.233).X - 212.3f) < .001f,
                "loaded zone change shifts grid anchor");
    }

    static void TestV3()
    {
        string v2 = Clip(true);
        string[] lines = v2.Split('\n');
        var v3 = new StringBuilder();
        v3.AppendLine("# avatar_mesh_hint=LocalBardBody");
        v3.AppendLine("# avatar_bone_count_hint=72");
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("# RebornCamera rolling pose clip v2",
                    StringComparison.Ordinal))
                v3.AppendLine("# RebornCamera rolling pose clip v3");
            else if (line.StartsWith("seconds\t", StringComparison.Ordinal))
                v3.AppendLine(line + "\tavatar_valid\tavatar_x\tavatar_y" +
                    "\tavatar_z\tavatar_qx\tavatar_qy\tavatar_qz\tavatar_qw");
            else if (char.IsDigit(line[0]))
                v3.AppendLine(line + "\t" + Pose(true, 10.5f));
            else v3.AppendLine(line);
        }

        ReplayClipData data = ReplayClipReader.Read(
            new StringReader(v3.ToString()), "v3.tsv");
        Check(data.Count == 6, "v3 frame count");
        Check(data.AvatarAnchorSamples == 6, "avatar anchors parsed");
        Check(data.AvatarMeshHint == "LocalBardBody", "mesh hint parsed");
        Check(data.AvatarBoneCountHint == 72, "bone count parsed");
        Check(data.Frames[1].Avatar.Valid &&
            Math.Abs(data.Frames[1].Avatar.X - 10.5f) < .001f,
            "avatar root position parsed");
        Check(data.TeleportCount == 1 && data.GapCount == 1,
            "v3 transition detection unchanged");
    }

    static void TestV4()
    {
        var lines = Clip(true).Split('\n');
        var text = new StringBuilder();
        text.AppendLine("# RebornCamera rolling pose clip v4");
        text.AppendLine("# avatar_mesh_hint=BodyParent hierarchy (not a mesh identifier)");
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("seconds\t", StringComparison.Ordinal))
            {
                foreach (string prefix in new[] {
                    "avatar", "avatar_root", "avatar_head",
                    "avatar_left", "avatar_right"
                })
                {
                    line += "\t" + prefix + "_valid\t" + prefix + "_x\t" +
                        prefix + "_y\t" + prefix + "_z\t" +
                        prefix + "_qx\t" + prefix + "_qy\t" +
                        prefix + "_qz\t" + prefix + "_qw";
                }
            }
            else if (char.IsDigit(line[0]))
            {
                line += "\t" + Pose(true, 7f);
                line += "\t" + Pose(true, 8f);
                line += "\t" + Pose(true, 9f);
                line += "\t" + Pose(true, 10f);
                line += "\t" + Pose(true, 11f);
            }
            text.AppendLine(line);
        }
        ReplayClipData clip = ReplayClipReader.Read(
            new StringReader(text.ToString()), "v4.tsv");
        Check(clip.Count == 6, "v4 frame count");
        Check(clip.AvatarAnchorSamples == 6, "v4 BodyParent samples");
        Check(clip.RigRootSamples == 6, "v4 rig root samples");
        Check(clip.ModelHeadSamples == 6, "v4 model head samples");
        Check(clip.ModelLeftSamples == 6, "v4 model left samples");
        Check(clip.ModelRightSamples == 6, "v4 model right samples");
        Check(clip.Frames[2].AvatarRigRoot.X == 8f, "root position");
        Check(clip.Frames[2].AvatarModelHead.X == 9f, "head position");
        Check(clip.Frames[2].AvatarModelRight.X == 11f, "right hand position");
        Check(clip.TeleportCount == 1, "v4 teleport safety");
    }

    static void Main()
    {
        Test(false);
        Test(true);
        TestV3();
        TestV4();
        bool refused = false;
        try
        {
            ReplayClipReader.Read(new StringReader("bad\tcolumns\n"),
                "invalid.tsv");
        }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "refuse invalid file");
        Console.WriteLine("ReplayClipReader tests passed (v1-v4, real rig tracks, teleport, gap, seek).");
    }
}
