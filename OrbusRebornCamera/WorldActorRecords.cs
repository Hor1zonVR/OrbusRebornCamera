using System;
using System.Globalization;
using System.IO;

namespace OrbusBetterMirror;

// An observation from the local Unity client, NOT authoritative server state.
// Keys are stable only for one recording process: Party Sync will later need
// a separate, verified identity and clock-alignment layer.
internal struct WorldActorObservation
{
    internal double Seconds;
    internal long UtcTicks;
    internal string ActorKey;
    internal string Category;
    internal string Prefab;
    internal string Zone;
    internal string Event; // appear, pose, leave
    internal float X, Y, Z, Qx, Qy, Qz, Qw;
    internal int AnimationHash;
    internal float AnimationTime;
}
internal sealed class WorldActorIdentity
{
    internal string ActorKey = "";
    internal string Category = "";
    internal string Prefab = "";
    internal string IdentitySource = "local Unity instance";
    // Display name deliberately unknown until a verified Orbus field is found.
    internal string DisplayName = "";
}

internal static class WorldActorRecords
{
    internal const int MaxActorIdentities = 32;
    internal const int MaxObservations = 180000;
    private static readonly CultureInfo Number = CultureInfo.InvariantCulture;

    internal static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string cleaned = value.Replace('\t', ' ').Replace('\r', ' ')
            .Replace('\n', ' ');
        return cleaned.Length > 100 ? cleaned.Substring(0, 100) : cleaned;
    }

    internal static void Write(TextWriter output, string sessionId,
        string clipId, WorldActorIdentity[] actors, WorldActorObservation[] data)
    {
        if (actors.Length > MaxActorIdentities ||
            data.Length > MaxObservations)
            throw new InvalidDataException("Actor capture exceeds safety limits.");

        output.WriteLine("# RebornCamera observed actor timeline v1");
        output.WriteLine("# session_id=" + Safe(sessionId));
        output.WriteLine("# clip_id=" + Safe(clipId));
        output.WriteLine("# timestamps=same clock as matching pose TSV");
        output.WriteLine("# identities=local instance only; NOT stable across game sessions");
        output.WriteLine("# player_names=not verified; blank by design");
        output.WriteLine("# npc_category=unverified visual/animation candidates");
        output.WriteLine("# events=appear pose leave; leave is no longer observed locally");
        output.WriteLine("# missing=world geometry, HP, damage, spells, audio, server actions");
        output.WriteLine("record\tactor_key\tcategory\tprefab\tidentity_source\tdisplay_name" +
            "\tseconds\tutc_ticks\tzone\tevent\tx\ty\tz\tqx\tqy\tqz\tqw" +
            "\tanim_state_hash\tanim_normalized_time");
        foreach (WorldActorIdentity actor in actors)
        {
            output.Write("actor\t");
            output.Write(Safe(actor.ActorKey)); output.Write('\t');
            output.Write(Safe(actor.Category)); output.Write('\t');
            output.Write(Safe(actor.Prefab)); output.Write('\t');
            output.Write(Safe(actor.IdentitySource)); output.Write('\t');
            output.Write(Safe(actor.DisplayName));
            output.WriteLine("\t\t\t\t\t\t\t\t\t\t\t\t");
        }
        foreach (WorldActorObservation item in data)
        {
            output.Write("sample\t"); output.Write(Safe(item.ActorKey));
            output.Write('\t'); output.Write(Safe(item.Category));
            output.Write('\t'); output.Write(Safe(item.Prefab));
            output.Write("\t\t\t");
            output.Write(item.Seconds.ToString("F4", Number)); output.Write('\t');
            output.Write(item.UtcTicks.ToString(Number)); output.Write('\t');
            output.Write(Safe(item.Zone)); output.Write('\t');
            output.Write(Safe(item.Event)); output.Write('\t');
            Append(output, item.X); Append(output, item.Y);
            Append(output, item.Z); Append(output, item.Qx);
            Append(output, item.Qy); Append(output, item.Qz);
            output.Write(item.Qw.ToString("R", Number)); output.Write('\t');
            output.Write(item.AnimationHash.ToString(Number)); output.Write('\t');
            output.WriteLine(item.AnimationTime.ToString("F5", Number));
        }
    }

    private static void Append(TextWriter writer, float value)
    {
        writer.Write(value.ToString("R", Number));
        writer.Write('\t');
    }
}
