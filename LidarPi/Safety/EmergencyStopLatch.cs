namespace LidarPi.Safety;

/// <summary>
/// Debounces the raw E-stop input and latches a trip until an explicit reset.
/// </summary>
/// <remarks>
/// <para><b>Asymmetric debounce:</b> a trip needs only <c>tripSamples</c> consecutive
/// "pressed" samples (20 ms by default) so the reaction is fast, while "released" must be
/// stable for <c>releaseStableMs</c> before a reset is accepted, so contact bounce while
/// twisting the mushroom head out cannot count as released.</para>
/// <para><b>Latch rule (from the senior-design WIRING.md):</b> releasing the button never
/// restarts anything by itself. The latch clears only when the button is released AND
/// <see cref="TryReset"/> is called (POST /api/estop/reset from the GUI). A reset while
/// the button is still pressed is refused with a message.</para>
/// <para>Thread safety: <see cref="Sample"/> runs on the safety thread and
/// <see cref="TryReset"/> on an API thread, so both take the same lock.</para>
/// </remarks>
public sealed class EmergencyStopLatch
{
    private readonly object _lock = new();
    private readonly int _tripSamples;
    private readonly TimeSpan _releaseStable;
    private int _pressedCount;            // consecutive raw "pressed" samples
    private DateTime? _releasedSinceUtc;  // when the raw input last changed to "released"

    /// <summary>Creates the latch.</summary>
    /// <param name="tripSamples">Consecutive pressed samples needed to trip.</param>
    /// <param name="releaseStableMs">Released time needed before a reset is allowed.</param>
    /// <param name="latchedAtStartup">True = start latched (restart prevention after a power cycle).</param>
    public EmergencyStopLatch(int tripSamples, int releaseStableMs, bool latchedAtStartup)
    {
        _tripSamples = Math.Max(1, tripSamples);
        _releaseStable = TimeSpan.FromMilliseconds(Math.Max(0, releaseStableMs));
        Latched = latchedAtStartup;
        LatchReason = latchedAtStartup ? "Power-up: press Reset E-Stop to enable motion" : null;
    }

    /// <summary>True while the E-stop is latched (motion must be disabled).</summary>
    public bool Latched { get; private set; }

    /// <summary>Debounced button state: true = pressed / wire broken.</summary>
    public bool Pressed { get; private set; }

    /// <summary>Raw (not debounced) state of the last sample.</summary>
    public bool RawPressed { get; private set; }

    /// <summary>Why the latch is set (shown in the GUI), or null.</summary>
    public string? LatchReason { get; private set; }

    /// <summary>Time of the most recent trip by the button.</summary>
    public DateTime? LastTripUtc { get; private set; }

    /// <summary>Number of trips since the service started.</summary>
    public int TripCount { get; private set; }

    /// <summary>Feeds one raw sample (called every safety-loop tick).</summary>
    /// <returns>True if this sample caused a NEW trip (so the caller can log it once).</returns>
    public bool Sample(bool rawPressed, DateTime nowUtc)
    {
        lock (_lock)
        {
            RawPressed = rawPressed;
            if (rawPressed)
            {
                _releasedSinceUtc = null;
                if (++_pressedCount < _tripSamples) return false;
                bool wasPressed = Pressed;
                Pressed = true;
                if (wasPressed) return false;   // same press as before: already latched and counted
                // Debounced released -> pressed edge: this is a trip.
                Latched = true;
                LatchReason = "E-stop button pressed (or wire broken)";
                LastTripUtc = nowUtc;
                TripCount++;
                return true;
            }

            _pressedCount = 0;
            _releasedSinceUtc ??= nowUtc;
            if (nowUtc - _releasedSinceUtc.Value >= _releaseStable) Pressed = false;
            return false;
        }
    }

    /// <summary>Tries to clear the latch (the explicit reset from the GUI).</summary>
    /// <returns>Whether the latch is now clear, and a message for the operator.</returns>
    public (bool Success, string Message) TryReset()
    {
        lock (_lock)
        {
            if (Pressed || RawPressed)
                return (false, "Reset refused: the E-stop button is still pressed (or its wire is broken). Release it first.");
            if (!Latched)
                return (true, "E-stop was not latched.");
            Latched = false;
            LatchReason = null;
            return (true, "E-stop reset. Motion is enabled again once the zone is not STOP and the LiDAR is healthy.");
        }
    }
}
