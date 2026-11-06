using LidarPi.Config;
using LidarPi.Lidar;

namespace LidarPi.Safety;

/// <summary>
/// Turns the closest obstacle distance into a <see cref="SafetyZone"/> with hysteresis.
/// </summary>
/// <remarks>
/// <para>Rule: moving to a MORE severe zone happens immediately at the threshold (safety
/// first). Moving back to a LESS severe zone needs the distance to clear the threshold by
/// <see cref="RuntimeConfig.HysteresisM"/>. Example with stop = 0.35 m and 5 cm:
/// red at 0.349 m, but red stays on until the obstacle is beyond 0.40 m. Without this, the
/// ~1 cm range noise of the A1 makes the LEDs flicker when something sits right at 0.35 m.</para>
/// <para>Starts in <see cref="SafetyZone.Stop"/>, so nothing is "clear" until a real scan says so.</para>
/// </remarks>
public sealed class ZoneClassifier
{
    /// <summary>Zone after the last update.</summary>
    public SafetyZone Current { get; private set; } = SafetyZone.Stop;

    /// <summary>Zone for a distance with fixed thresholds and no hysteresis (used per object).</summary>
    public static SafetyZone ZoneFor(double distanceM, RuntimeConfig cfg) =>
        distanceM < cfg.StopDistanceM ? SafetyZone.Stop
        : distanceM < cfg.CautionDistanceM ? SafetyZone.Caution
        : SafetyZone.Clear;

    /// <summary>Updates the zone from the closest obstacle in the monitoring sector.</summary>
    /// <param name="closestM">Closest distance, or null if nothing is in range inside the sector.</param>
    /// <param name="cfg">Thresholds and hysteresis.</param>
    /// <returns>The new current zone.</returns>
    public SafetyZone Update(double? closestM, RuntimeConfig cfg)
    {
        if (closestM is null)
        {
            // Nothing within MaxRange inside the sector. (A blinded sensor is caught separately
            // by the "too few valid points" fault in SafetyMonitor.)
            Current = SafetyZone.Clear;
            return Current;
        }
        double d = closestM.Value;
        SafetyZone raw = ZoneFor(d, cfg);
        // Zone we would be allowed to improve to: thresholds pushed out by the hysteresis band.
        SafetyZone improveTo = ZoneFor(d - cfg.HysteresisM, cfg);
        Current = raw >= Current ? raw                       // worse (or same): take it at once
                : (SafetyZone)Math.Min((int)Current, (int)improveTo);   // better: only past the band
        return Current;
    }

    /// <summary>Forces STOP (used on any fault) so recovery also goes through the hysteresis band.</summary>
    public void ForceStop() => Current = SafetyZone.Stop;
}
