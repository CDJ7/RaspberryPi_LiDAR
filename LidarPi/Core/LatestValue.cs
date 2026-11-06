using System.Diagnostics;

namespace LidarPi.Core;

/// <summary>
/// Thread-safe "latest value" holder used to hand data from one thread to another
/// (port of <c>agv_pi/core/latest.py</c> from the senior design repo).
/// </summary>
/// <remarks>
/// WHY not a queue: the safety loop and the REST API only ever care about the newest
/// LiDAR revolution. A queue would grow if a consumer is slow and the safety decision
/// would then be made on old data. Here a producer simply overwrites the previous value,
/// so a slow consumer can never make the producer block or fall behind.
/// </remarks>
/// <typeparam name="T">Immutable value type/record that is published (must not be mutated after publishing).</typeparam>
public sealed class LatestValue<T> where T : class
{
    // A single lock is plenty: publish/read happen ~10-100 times per second.
    private readonly object _lock = new();
    private T? _value;
    private long _stampTicks;   // Stopwatch timestamp of the last publish (monotonic, immune to clock changes)
    private long _sequence;     // increments on every publish; lets readers detect "new since last time"

    /// <summary>Stores <paramref name="value"/> as the newest value and timestamps it.</summary>
    public void Publish(T value)
    {
        lock (_lock)
        {
            _value = value;
            _stampTicks = Stopwatch.GetTimestamp();
            _sequence++;
        }
    }

    /// <summary>Returns the newest value, or <c>null</c> if nothing was ever published.</summary>
    public T? Get()
    {
        lock (_lock) { return _value; }
    }

    /// <summary>
    /// Returns the newest value together with its age. The age is what the fail-safe
    /// "LiDAR stale" check uses, so it is measured with a monotonic clock.
    /// </summary>
    /// <returns>The value (or null) and its age; age is <see cref="TimeSpan.MaxValue"/> if nothing was published.</returns>
    public (T? Value, TimeSpan Age, long Sequence) GetWithAge()
    {
        lock (_lock)
        {
            if (_sequence == 0) return (null, TimeSpan.MaxValue, 0);
            return (_value, Stopwatch.GetElapsedTime(_stampTicks), _sequence);
        }
    }
}
