using System;
using OrbusBetterMirror;

static class ReplayRingSmokeTest
{
    private readonly record struct Frame(double Time, int Value);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAILED: " + message);
    }

    public static void Main()
    {
        var buffer = new ReplayRingBuffer<Frame>(3, 5, frame => frame.Time);
        Check(buffer.Count == 0 && buffer.Snapshot(0).Length == 0,
            "empty buffer");
        buffer.Add(new Frame(0, 10));
        buffer.Add(new Frame(1, 11));
        buffer.Add(new Frame(2, 12));
        buffer.Add(new Frame(3, 13));
        var copy = buffer.Snapshot(3);
        Check(copy.Length == 3 && copy[0].Value == 11 &&
            copy[2].Value == 13, "fixed-capacity wraparound");

        copy[0] = new Frame(999, 999);
        Check(buffer.Snapshot(3)[0].Value == 11,
            "saving a clip cannot mutate the live buffer");

        buffer.Add(new Frame(7, 17));
        copy = buffer.Snapshot(7);
        Check(copy.Length == 2 && copy[0].Value == 13 &&
            copy[1].Value == 17, "time-window eviction");

        Check(buffer.Snapshot(13).Length == 0,
            "samples expire even during missing camera/scene load");

        buffer.Add(new Frame(14, 18));
        buffer.Add(new Frame(14, 19)); // multiple events at one time permitted
        Check(buffer.Snapshot(14).Length == 2, "equal timestamps supported");

        bool threw = false;
        try { buffer.Add(new Frame(13, 5)); }
        catch (ArgumentException) { threw = true; }
        Check(threw, "reject backwards timestamp");

        buffer.Clear();
        Check(buffer.Count == 0, "clear on disable");
        Console.WriteLine("ReplayRingBuffer tests passed");
    }
}
