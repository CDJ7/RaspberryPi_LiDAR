namespace LidarPi.Lidar;

/// <summary>
/// One raw RPLIDAR measurement exactly as the sensor reports it.
/// </summary>
/// <param name="Quality">Signal quality 0-63 (upper 6 bits of node byte 0). Low quality = unreliable return.</param>
/// <param name="AngleDeg">Raw angle 0-360 degrees, increasing CLOCKWISE seen from above (RPLIDAR convention).</param>
/// <param name="DistanceMm">Distance in millimetres; 0 means "no return" for this beam.</param>
public readonly record struct RawMeasurement(byte Quality, float AngleDeg, float DistanceMm);

/// <summary>
/// One filtered point in the robot frame (x forward, y left, angles counter-clockwise).
/// </summary>
/// <param name="AngleDeg">Robot-frame angle in (-180, 180]: 0 = forward, +90 = left, -90 = right.</param>
/// <param name="RangeM">Distance from the LiDAR in metres.</param>
public readonly record struct ScanPoint(float AngleDeg, float RangeM)
{
    /// <summary>X coordinate (metres, forward positive). Used for clustering and plotting.</summary>
    public float X => RangeM * MathF.Cos(AngleDeg * MathF.PI / 180f);

    /// <summary>Y coordinate (metres, left positive).</summary>
    public float Y => RangeM * MathF.Sin(AngleDeg * MathF.PI / 180f);
}

/// <summary>
/// Safety zone derived from the closest obstacle. The order matters: a larger value
/// is more severe, which lets the hysteresis logic compare zones with &lt; and &gt;.
/// </summary>
public enum SafetyZone
{
    /// <summary>Nothing closer than the caution distance (green LED).</summary>
    Clear = 0,
    /// <summary>Obstacle between the stop and caution distances (yellow LED).</summary>
    Caution = 1,
    /// <summary>Obstacle closer than the stop distance, or any fault (red LED). Motion is disabled.</summary>
    Stop = 2,
}

/// <summary>An object found by grouping neighbouring scan points (see <see cref="ObjectClusterer"/>).</summary>
/// <param name="Id">1-based ID, assigned closest-first within one scan (IDs are per scan, not tracked).</param>
/// <param name="DistanceM">Distance to the object's closest point.</param>
/// <param name="BearingDeg">Bearing of the closest point, 0-360 counter-clockwise from forward (90 = left).</param>
/// <param name="WidthM">Approximate width: straight-line distance between the cluster's first and last point.</param>
/// <param name="PointCount">How many scan points belong to the object.</param>
/// <param name="InSector">True if the closest point lies inside the configured monitoring sector.</param>
/// <param name="Zone">Zone this object alone would cause (fixed thresholds, no hysteresis).</param>
/// <param name="Outline">Up to ~24 points of the object (robot frame x/y in metres) so the GUI can highlight it.</param>
public sealed record DetectedObject(
    int Id,
    float DistanceM,
    float BearingDeg,
    float WidthM,
    int PointCount,
    bool InSector,
    SafetyZone Zone,
    IReadOnlyList<float[]> Outline);

/// <summary>
/// The result of processing one full LiDAR revolution. Immutable so it can be shared
/// between threads through <see cref="Core.LatestValue{T}"/> without copying.
/// </summary>
/// <param name="TimestampUtc">When the revolution finished.</param>
/// <param name="RawCount">Number of raw measurements in the revolution (before filtering).</param>
/// <param name="Points">Filtered points sorted by angle.</param>
/// <param name="Objects">Detected objects sorted closest first.</param>
/// <param name="ClosestInSector">Closest point inside the monitoring sector, or null if the sector is empty.</param>
public sealed record ProcessedScan(
    DateTime TimestampUtc,
    int RawCount,
    IReadOnlyList<ScanPoint> Points,
    IReadOnlyList<DetectedObject> Objects,
    ScanPoint? ClosestInSector);

/// <summary>Information returned by the RPLIDAR GET_INFO request.</summary>
/// <param name="Model">Model byte. A1-series units report 0x1x (A1M8 is usually 0x18).</param>
/// <param name="Firmware">Firmware version as "major.minor".</param>
/// <param name="Hardware">Hardware revision byte.</param>
/// <param name="SerialNumber">16-byte serial number as hex.</param>
public sealed record LidarDeviceInfo(byte Model, string Firmware, byte Hardware, string SerialNumber)
{
    /// <summary>Human-readable model name derived from the model byte (upper nibble = series).</summary>
    public string ModelName => (Model >> 4) switch
    {
        1 => $"RPLIDAR A1 (model 0x{Model:X2})",
        2 => $"RPLIDAR A2 (model 0x{Model:X2})",
        3 => $"RPLIDAR A3 (model 0x{Model:X2})",
        _ => $"Unknown RPLIDAR (model 0x{Model:X2})",
    };

    /// <summary>
    /// True for the A1 series. Matters because only the A1 switches its motor with DTR;
    /// A2/A3 use a PWM command instead (not implemented here).
    /// </summary>
    public bool IsA1Series => (Model >> 4) == 1;
}

/// <summary>Result of the GET_HEALTH request.</summary>
/// <param name="Status">"Good", "Warning", "Error" (or "Unknown").</param>
/// <param name="ErrorCode">Device-specific error code (0 when healthy).</param>
public sealed record LidarHealth(string Status, int ErrorCode);
