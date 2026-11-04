using System;

namespace OrbusBetterMirror;

/// <summary>
/// A bounded, in-memory time window. Independent of Unity so it can be
/// tested without launching Orbus or using private game interop assemblies.
/// All callers must use it from the same thread (the Unity main thread).
/// </summary>
internal sealed class ReplayRingBuffer<T>
{
    private readonly T[] _values;
    private readonly Func<T, double> _timestamp;
    private readonly double _windowSeconds;
    private int _first;
    private int _count;

    internal ReplayRingBuffer(int capacity, double windowSeconds,
        Func<T, double> timestamp)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!(windowSeconds > 0) || double.IsInfinity(windowSeconds))
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));

        _values = new T[capacity];
        _windowSeconds = windowSeconds;
        _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
    }

    internal int Count => _count;
    internal int Capacity => _values.Length;
    internal double WindowSeconds => _windowSeconds;

    internal double? OldestTime =>
        _count == 0 ? null : _timestamp(_values[_first]);

    internal double? NewestTime =>
        _count == 0 ? null :
        _timestamp(_values[(_first + _count - 1) % _values.Length]);

    internal void Add(T value)
    {
        double now = _timestamp(value);
        if (double.IsNaN(now) || double.IsInfinity(now))
            throw new ArgumentOutOfRangeException(nameof(value), "Invalid timestamp.");

        double? last = NewestTime;
        if (last.HasValue && now < last.Value)
            throw new ArgumentException("Timestamps must be monotonic.", nameof(value));

        if (_count == _values.Length)
            RemoveOldest();

        _values[(_first + _count) % _values.Length] = value;
        _count++;
        Trim(now);
    }

    // Old samples expire even if the camera is unavailable after a zone switch.
    internal void Trim(double now)
    {
        while (_count > 0 && now - _timestamp(_values[_first]) > _windowSeconds)
            RemoveOldest();
    }

    internal T[] Snapshot(double now)
    {
        Trim(now);
        T[] snapshot = new T[_count];
        for (int i = 0; i < _count; i++)
            snapshot[i] = _values[(_first + i) % _values.Length];
        return snapshot;
    }

    internal void Clear()
    {
        Array.Clear(_values, 0, _values.Length);
        _first = 0;
        _count = 0;
    }

    private void RemoveOldest()
    {
        if (_count == 0) return;
        _values[_first] = default!;
        _first = (_first + 1) % _values.Length;
        _count--;
    }
}
