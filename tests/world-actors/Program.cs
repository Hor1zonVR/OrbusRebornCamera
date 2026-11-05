using System;
using System.IO;
using System.Linq;
using OrbusBetterMirror;

internal static class WorldActorTests
{
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
    }

    private static WorldActorObservation Sample(double t, string key,
        string eventKind, float x)
    {
        return new WorldActorObservation
        {
            Seconds=t, UtcTicks=638980000000000000L+(long)(t*10000000),
            ActorKey=key, Category=key=="actor-1"?"remote_player_candidate":
                "animated_world_candidate",
            Prefab=key=="actor-1"?"OtherPlayerv2(Clone)":"AnimatedPrefab",
            Zone="ZoneHighsteppe", Event=eventKind,
            X=x, Y=2, Z=3, Qw=1,
            AnimationHash=10, AnimationTime=.5f
        };
    }

    internal static void Main()
    {
        var actors = new[] {
            new WorldActorIdentity { ActorKey="actor-1",
                Category="remote_player_candidate",
                Prefab="OtherPlayerv2(Clone)" },
            new WorldActorIdentity { ActorKey="actor-2",
                Category="animated_world_candidate",
                Prefab="AnimatedPrefab" }
        };
        var observations = new[] {
            Sample(10, "actor-1", "appear", 1),
            Sample(10, "actor-2", "appear", 2),
            Sample(10.1, "actor-1", "pose", 3),
            Sample(10.1, "actor-2", "pose", 4),
            Sample(10.2, "actor-2", "leave", 0),
            Sample(10.3, "actor-1", "pose", 6)
        };
        var writer = new StringWriter();
        WorldActorRecords.Write(writer, "session", "clip", actors, observations);
        string data = writer.ToString();
        Check(data.Contains("remote_player_candidate"), "player category");
        Check(data.Contains("animated_world_candidate"), "world actor category");
        Check(data.Contains("# player_names=not verified"), "identity caveat");
        var timeline = WorldActorTimeline.Read(new StringReader(data));
        Check(timeline.ActorCount == 2, "two actors");
        Check(timeline.ObservationCount == 6, "six samples");
        Check(timeline.VisibleAt(9.9).Length == 0, "no actor before appearance");
        Check(timeline.VisibleAt(10.15).Length == 2,
            "both present before leave");
        Check(timeline.VisibleAt(10.25).Length == 1,
            "leaving NPC is not frozen in place");
        Check(timeline.VisibleAt(11).Length == 0,
            "stale remote player is not frozen forever");
        var jumpSamples = new[] {
            Sample(20, "actor-1", "appear", 1),
            Sample(20.1, "actor-1", "pose", 240)
        };
        var jumpWriter = new StringWriter();
        WorldActorRecords.Write(jumpWriter, "session", "jump",
            new[]{actors[0]}, jumpSamples);
        var jumpTimeline = WorldActorTimeline.Read(
            new StringReader(jumpWriter.ToString()));
        Check(Math.Abs(jumpTimeline.VisibleAt(20.05)[0].X - 1f) < .01f,
            "no interpolation across recorded teleport");
        Check(WorldActorRecords.Safe("abc\tdef\n").IndexOf('\n') < 0,
            "sanitize name and prefab fields");
        bool refused = false;
        try
        {
            WorldActorTimeline.Read(new StringReader(
                "sample\ta\tb\tc\td\n"));
        }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "truncated actor rows rejected");
        Console.WriteLine("World actor export and offline visibility tests passed.");
    }
}
