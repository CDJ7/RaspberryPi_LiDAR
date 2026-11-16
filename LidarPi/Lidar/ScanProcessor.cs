using LidarPi.Config;

namespace LidarPi.Lidar;

/// <summary>
/// Turns one raw revolution into a <see cref="ProcessedScan"/>: convention conversion,
/// filtering, closest point in the monitoring sector and object clustering.
/// (Port of <c>ScanConverter</c> in <c>agv_pi/lidar/scan.py</c>.)
/// </summary>
/// <remarks>
/// This is the ONE place that knows the RPLIDAR's conventions (clockwise angles, mm,
/// quality). Everything after it works in the robot frame: 0 deg = forward, angles
/// counter-clockwise, so +90 deg = left. That is why the <c>--check</c> orientation test
/// (object on the left must show up on the left) only needs to be passed once.
/// </remarks>
public static class ScanProcessor
{
    /// <summary>Converts and filters a revolution, then finds the closest point and the objects.</summary>
    /// <param name="raw">Raw measurements of one revolution.</param>
    /// <param name="cfg">Runtime config (offset, sector, clustering) - read once so a change mid-scan cannot mix values.</param>
    /// <param name="lidar">Static filter settings (range limits, quality, direction).</param>
    /// <returns>The processed, immutable scan.</returns>
    public static ProcessedScan Process(IReadOnlyList<RawMeasurement> raw, RuntimeConfig cfg, LidarOptions lidar)
    {
        var points = new List<ScanPoint>(raw.Count);
        foreach (RawMeasurement m in raw)
        {
            float r = m.DistanceMm / 1000f;
            if (m.Quality < lidar.MinQuality || r < lidar.MinRangeM || r > lidar.MaxRangeM) continue;
            // Clockwise -> counter-clockwise, then rotate by the mounting offset.
            double a = (lidar.Clockwise ? -m.AngleDeg : m.AngleDeg) + cfg.AngleOffsetDeg;
            points.Add(new ScanPoint(NormalizeDeg(a), r));
        }
        points.Sort((p, q) => p.AngleDeg.CompareTo(q.AngleDeg));

        ScanPoint? closest = null;
        foreach (ScanPoint p in points)
        {
            if (IsInSector(p.AngleDeg, cfg) && (closest is null || p.RangeM < closest.Value.RangeM)) closest = p;
        }

        return new ProcessedScan(DateTime.UtcNow, raw.Count, points, ObjectClusterer.Cluster(points, cfg), closest);
    }

    /// <summary>True if a robot-frame angle is inside the configured monitoring sector.</summary>
    public static bool IsInSector(float angleDeg, RuntimeConfig cfg) =>
        cfg.SectorMode == SectorMode.Full || Math.Abs(angleDeg) <= cfg.FrontHalfAngleDeg;

    /// <summary>
    /// Robot-frame angle (-180..180) to the bearing shown to the user: 0-360 counter-clockwise
    /// from forward (90 = left, 270 = right). WHY 0-360: it fits the LCD's 3-digit field.
    /// </summary>
    public static float ToBearing(float angleDeg)
    {
        float b = angleDeg < 0 ? angleDeg + 360f : angleDeg;
        return b >= 359.5f ? 0f : b;   // so rounding never prints "360"
    }

    /// <summary>Wraps any angle in degrees into (-180, 180].</summary>
    public static float NormalizeDeg(double deg)
    {
        double a = deg % 360.0;
        if (a <= -180.0) a += 360.0;
        else if (a > 180.0) a -= 360.0;
        return (float)a;
    }
}
