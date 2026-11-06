using LidarPi.Config;
using LidarPi.Safety;

namespace LidarPi.Lidar;

/// <summary>
/// Groups the points of one revolution into objects: consecutive points (by angle) that
/// are closer together than the gap belong to the same object.
/// </summary>
/// <remarks>
/// WHY this simple method: a 2D scan is already ordered by angle, so a single pass that
/// splits wherever two neighbours are far apart is O(n), needs no tuning beyond one gap
/// distance, and is easy to explain. It cannot separate two objects that touch, which is
/// acceptable for a proximity monitor.
/// </remarks>
public static class ObjectClusterer
{
    private const int MaxOutlinePoints = 24;   // enough for the GUI highlight, keeps /api/scan small

    /// <summary>Clusters angle-sorted points into objects, sorted closest first.</summary>
    /// <param name="points">Filtered points, sorted by angle.</param>
    /// <param name="cfg">Supplies the gap, minimum cluster size, thresholds and sector.</param>
    /// <returns>Objects with IDs 1..n in closest-first order.</returns>
    public static IReadOnlyList<DetectedObject> Cluster(IReadOnlyList<ScanPoint> points, RuntimeConfig cfg)
    {
        if (points.Count == 0) return [];
        float gap = (float)cfg.ClusterGapM;

        // 1) Split into runs of neighbouring points.
        var clusters = new List<List<ScanPoint>> { new() { points[0] } };
        for (int i = 1; i < points.Count; i++)
        {
            if (Distance(points[i - 1], points[i]) > gap) clusters.Add([]);
            clusters[^1].Add(points[i]);
        }

        // 2) The scan wraps around at +/-180 degrees: an object straight behind the robot is
        //    split into the last and the first run. Join them if their ends touch.
        if (clusters.Count > 1 && Distance(points[^1], points[0]) <= gap)
        {
            clusters[^1].AddRange(clusters[0]);
            clusters.RemoveAt(0);
        }

        // 3) Describe each cluster that has enough points to be real.
        var result = new List<DetectedObject>();
        foreach (List<ScanPoint> c in clusters)
        {
            if (c.Count < cfg.MinClusterPoints) continue;   // a few stray points = noise
            ScanPoint closest = c.MinBy(p => p.RangeM);
            float width = Distance(c[0], c[^1]);
            int step = Math.Max(1, c.Count / MaxOutlinePoints);
            var outline = new List<float[]>();
            for (int i = 0; i < c.Count; i += step) outline.Add([MathF.Round(c[i].X, 3), MathF.Round(c[i].Y, 3)]);
            result.Add(new DetectedObject(
                Id: 0,
                DistanceM: closest.RangeM,
                BearingDeg: ScanProcessor.ToBearing(closest.AngleDeg),
                WidthM: width,
                PointCount: c.Count,
                InSector: ScanProcessor.IsInSector(closest.AngleDeg, cfg),
                Zone: ZoneClassifier.ZoneFor(closest.RangeM, cfg),
                Outline: outline));
        }

        // 4) Closest first, then number them so ID 1 is always the most important object.
        return result.OrderBy(o => o.DistanceM).Select((o, i) => o with { Id = i + 1 }).ToList();
    }

    /// <summary>Straight-line distance between two scan points in metres.</summary>
    private static float Distance(ScanPoint a, ScanPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
