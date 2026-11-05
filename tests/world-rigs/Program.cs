using System;
using System.IO;
using OrbusBetterMirror;

internal static class WorldRigTests
{
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
    }

    private static WorldRigBonePose Bone(float x) => new()
    {
        X = x, Y = 2f, Z = 3f, Qw = 1f
    };

    private static WorldRigSample Sample(string key, double seconds, float baseX)
        => new()
        {
            ActorKey = key, Seconds = seconds,
            Bones = new[] { Bone(baseX), Bone(baseX + .2f), Bone(baseX + .5f) }
        };

    public static void Main()
    {
        var pet = new WorldRigDefinition
        {
            ActorKey = "actor-6", Category = "animated_world_candidate",
            MeshName = "TigerDragon",
            BonePaths = new[] { "root", "root/spine", "root/spine/head" },
            ParentIndices = new[] { -1, 0, 1 }
        };
        var npc = new WorldRigDefinition
        {
            ActorKey = "actor-21", Category = "animated_world_candidate",
            MeshName = "NPC",
            BonePaths = new[] { "root", "root/arm", "root/arm/hand" },
            ParentIndices = new[] { -1, 0, 1 }
        };
        var inputs = new[]
        {
            Sample("actor-6", 1d, 10),
            Sample("actor-21", 1d, 15),
            Sample("actor-6", 1.2d, 11),
            Sample("actor-21", 1.2d, 15.5f),
            Sample("actor-6", 1.4d, 210), // teleport; no gliding
            Sample("actor-21", 1.4d, 16)
        };
        using var data = new MemoryStream();
        WorldRigCodec.Write(data, "session-1", "clip-1",
            new[] { pet, npc }, inputs);
        Check(data.Length > 0 && data.Length < 8192,
            "binary rig file compact");
        data.Position = 0;
        WorldRigTimeline loaded = WorldRigCodec.Read(data);
        Check(loaded.RigCount == 2 && loaded.SampleCount == 6,
            "two independent actor bone tracks preserved");
        Check(loaded.SessionId == "session-1" &&
            loaded.ClipId == "clip-1", "clip/session ID alignment");
        Check(loaded.TryAt("actor-6", 1.1d, out var def,
            out var a, out var b, out float blend) &&
            def != null && def.MeshName == "TigerDragon" &&
            def.ParentIndices[2] == 1 &&
            a != null && b != null &&
            Math.Abs(blend - .5f) < .001f,
            "actual bone topology and smooth playback");
        Check(loaded.TryAt("actor-6", 1.3d,
            out _, out var beforeJump, out var afterJump, out float jumpBlend)
            && beforeJump != null && afterJump == null && jumpBlend == 0f,
            "do not glide captured bones across large teleports");
        Check(!loaded.TryAt("actor-6", 2d,
            out _, out _, out _, out _),
            "missing bone stream not frozen forever");
        Check(!loaded.TryAt("actor-6", .99d,
            out _, out _, out _, out _),
            "not visible before first bone sample");
        Check(loaded.TryAt("actor-21", 1.1d,
            out _, out var npcFrame, out _, out _) &&
            npcFrame != null && Math.Abs(npcFrame.Bones[0].X - 15f) < .01f,
            "second actor has its own bone track");
        bool bad = false;
        try
        {
            WorldRigCodec.Write(new MemoryStream(), "s", "c",
                new[] { new WorldRigDefinition {
                    ActorKey = "bad", BonePaths = new [] { "root" },
                    ParentIndices = new[] { 0 } // self-parent
                } }, Array.Empty<WorldRigSample>());
        }
        catch (InvalidDataException) { bad = true; }
        Check(bad, "reject invalid skeleton parent graph");
        data.SetLength(8);
        data.Position = 0;
        bad = false;
        try { WorldRigCodec.Read(data); }
        catch (InvalidDataException) { bad = true; }
        Check(bad, "reject truncated binary rig file");
        Console.WriteLine(
            "World rig bone record/read/interpolation/teleport safety tests passed.");
    }
}
