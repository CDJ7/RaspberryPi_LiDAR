namespace LidarPC.Api;

// JSON contract of the Pi's REST API, mirrored from LidarPi/Api/ApiDtos.cs.
// WHY a copy instead of a shared project: the assignment asks for exactly one folder per
// program, each buildable on its own. The types are plain records, so keeping the two
// copies in sync is simple. Property names match the camelCase JSON case-insensitively.

/// <summary>Safety zone reported by the Pi (ordered from least to most severe).</summary>
public enum SafetyZone
{
    /// <summary>Nothing within the caution distance (green).</summary>
    Clear,
    /// <summary>Obstacle between the stop and caution distances (yellow).</summary>
    Caution,
    /// <summary>Obstacle within the stop distance, or a fault (red).</summary>
    Stop,
}

/// <summary>LED mode on the Pi.</summary>
public enum LedMode
{
    /// <summary>LEDs follow the safety state.</summary>
    Auto,
    /// <summary>LEDs set by hand from this GUI.</summary>
    Test,
}

/// <summary>Error body of a failed request.</summary>
/// <param name="Error">Message from the Pi.</param>
public sealed record ApiErrorBody(string? Error);

/// <summary>Success body with a message.</summary>
/// <param name="Message">Message from the Pi.</param>
public sealed record ApiMessage(string Message);

/// <summary>LED state.</summary>
/// <param name="Mode">Auto or Test.</param>
/// <param name="Green">Green lit.</param>
/// <param name="Yellow">Yellow lit.</param>
/// <param name="Red">Red lit.</param>
/// <param name="TestSecondsRemaining">Seconds until test mode ends.</param>
public sealed record LedState(LedMode Mode, bool Green, bool Yellow, bool Red, int TestSecondsRemaining);

/// <summary>Live status from the safety loop.</summary>
/// <param name="TimestampUtc">When it was computed.</param>
/// <param name="Zone">Current zone.</param>
/// <param name="ClosestM">Closest obstacle in the sector.</param>
/// <param name="BearingDeg">Its bearing (0-360, counter-clockwise from forward).</param>
/// <param name="MotionEnabled">Motion-enable output.</param>
/// <param name="EStopLatched">E-stop latched.</param>
/// <param name="EStopPressed">Button pressed (debounced).</param>
/// <param name="EStopReason">Why it is latched.</param>
/// <param name="LidarFault">LiDAR data not trustworthy.</param>
/// <param name="FaultMessage">Fault description.</param>
/// <param name="HardwareFaults">GPIO/LCD problems.</param>
/// <param name="Leds">LED state.</param>
/// <param name="LcdLine1">LCD line 1.</param>
/// <param name="LcdLine2">LCD line 2.</param>
/// <param name="ShuttingDown">Shutdown in progress.</param>
/// <param name="ShutdownError">Shutdown failure, if any.</param>
/// <param name="Simulate">Pi app runs with --simulate.</param>
/// <param name="DefaultPassword">Default password still in use.</param>
public sealed record StatusResponse(
    DateTime TimestampUtc, SafetyZone Zone, double? ClosestM, double? BearingDeg, bool MotionEnabled,
    bool EStopLatched, bool EStopPressed, string? EStopReason, bool LidarFault, string? FaultMessage,
    IReadOnlyList<string> HardwareFaults, LedState Leds, string LcdLine1, string LcdLine2,
    bool ShuttingDown, string? ShutdownError, bool Simulate, bool DefaultPassword);

/// <summary>An object detected by the Pi.</summary>
/// <param name="Id">ID within this scan (1 = closest).</param>
/// <param name="DistanceM">Distance to its closest point.</param>
/// <param name="BearingDeg">Bearing of its closest point.</param>
/// <param name="WidthM">Approximate width.</param>
/// <param name="PointCount">Number of scan points.</param>
/// <param name="InSector">Inside the monitoring sector.</param>
/// <param name="Zone">Zone this object alone causes.</param>
/// <param name="Outline">Up to ~24 [x, y] points for highlighting.</param>
public sealed record DetectedObject(int Id, float DistanceM, float BearingDeg, float WidthM, int PointCount, bool InSector,
    SafetyZone Zone, IReadOnlyList<float[]> Outline)
{
    /// <summary>X (forward) of the closest point, from distance and bearing.</summary>
    public double X => DistanceM * Math.Cos(BearingDeg * Math.PI / 180.0);

    /// <summary>Y (left) of the closest point.</summary>
    public double Y => DistanceM * Math.Sin(BearingDeg * Math.PI / 180.0);
}

/// <summary>Response of GET /api/scan.</summary>
/// <param name="Status">Live status.</param>
/// <param name="ScanTimestampUtc">When the revolution arrived.</param>
/// <param name="Points">[x, y] points in metres (x forward, y left).</param>
/// <param name="Objects">Objects, closest first.</param>
/// <param name="StopDistanceM">STOP threshold.</param>
/// <param name="CautionDistanceM">CAUTION threshold.</param>
/// <param name="SectorMode">"Full" or "Front".</param>
/// <param name="FrontHalfAngleDeg">Front sector half-angle.</param>
/// <param name="ScanRateHz">LiDAR revolutions per second.</param>
public sealed record ScanResponse(StatusResponse Status, DateTime? ScanTimestampUtc, IReadOnlyList<float[]> Points,
    IReadOnlyList<DetectedObject> Objects, double StopDistanceM, double CautionDistanceM, string SectorMode,
    double FrontHalfAngleDeg, double ScanRateHz);

/// <summary>Response of GET /api/estop.</summary>
/// <param name="Pressed">Debounced button state.</param>
/// <param name="RawPressed">Raw reading.</param>
/// <param name="RawLevel">"HIGH" or "LOW".</param>
/// <param name="Latched">Latch state.</param>
/// <param name="Reason">Latch reason.</param>
/// <param name="LastTripUtc">Last trip time.</param>
/// <param name="TripCount">Trips since start.</param>
/// <param name="InputFault">GPIO error on the pin.</param>
public sealed record EStopState(bool Pressed, bool RawPressed, string RawLevel, bool Latched, string? Reason,
    DateTime? LastTripUtc, int TripCount, string? InputFault);

/// <summary>Response of GET/PUT /api/config.</summary>
/// <param name="StopDistanceM">STOP threshold.</param>
/// <param name="CautionDistanceM">CAUTION threshold.</param>
/// <param name="HysteresisM">Hysteresis.</param>
/// <param name="SectorMode">"Full" or "Front".</param>
/// <param name="FrontHalfAngleDeg">Front half-angle.</param>
/// <param name="AngleOffsetDeg">Mounting offset.</param>
/// <param name="ClusterGapM">Clustering gap.</param>
/// <param name="MinClusterPoints">Minimum points per object.</param>
/// <param name="LcdI2cAddress">LCD address (decimal).</param>
/// <param name="Username">API user.</param>
/// <param name="IsDefaultPassword">Default password in use.</param>
/// <param name="ConfigPath">Path of the file on the Pi.</param>
public sealed record ConfigResponse(double StopDistanceM, double CautionDistanceM, double HysteresisM, string SectorMode,
    double FrontHalfAngleDeg, double AngleOffsetDeg, double ClusterGapM, int MinClusterPoints, int LcdI2cAddress,
    string Username, bool IsDefaultPassword, string ConfigPath);

/// <summary>Body of PUT /api/config (null fields are left unchanged on the Pi).</summary>
/// <param name="StopDistanceM">STOP threshold.</param>
/// <param name="CautionDistanceM">CAUTION threshold.</param>
/// <param name="HysteresisM">Hysteresis.</param>
/// <param name="SectorMode">"Full" or "Front".</param>
/// <param name="FrontHalfAngleDeg">Front half-angle.</param>
/// <param name="AngleOffsetDeg">Mounting offset.</param>
/// <param name="ClusterGapM">Clustering gap.</param>
/// <param name="MinClusterPoints">Minimum points per object.</param>
/// <param name="LcdI2cAddress">LCD address (decimal).</param>
public sealed record ConfigUpdate(double? StopDistanceM, double? CautionDistanceM, double? HysteresisM, string? SectorMode,
    double? FrontHalfAngleDeg, double? AngleOffsetDeg, double? ClusterGapM, int? MinClusterPoints, int? LcdI2cAddress);

/// <summary>OS details inside GET /api/system.</summary>
/// <param name="Hostname">Pi host name.</param>
/// <param name="OsVersion">OS version.</param>
/// <param name="Kernel">Kernel version.</param>
/// <param name="MemAvailableMb">Available RAM.</param>
/// <param name="MemTotalMb">Total RAM.</param>
/// <param name="SystemUptimeSeconds">Seconds since boot.</param>
/// <param name="ServiceUptimeSeconds">Seconds since the Pi program started.</param>
/// <param name="IpAddresses">IPv4 addresses.</param>
/// <param name="CpuTemperatureC">SoC temperature.</param>
/// <param name="DotnetVersion">.NET runtime on the Pi.</param>
public sealed record SystemInfo(string Hostname, string OsVersion, string Kernel, double MemAvailableMb, double MemTotalMb,
    double SystemUptimeSeconds, double ServiceUptimeSeconds, IReadOnlyList<string> IpAddresses, double? CpuTemperatureC,
    string DotnetVersion);

/// <summary>LiDAR details inside GET /api/system.</summary>
/// <param name="Device">Serial device or "simulator".</param>
/// <param name="Connected">Receiving data.</param>
/// <param name="Model">Model name.</param>
/// <param name="Firmware">Firmware version.</param>
/// <param name="Hardware">Hardware revision.</param>
/// <param name="SerialNumber">Serial number.</param>
/// <param name="Health">Health status.</param>
/// <param name="HealthCode">Health code.</param>
/// <param name="ScanRateHz">Revolutions per second.</param>
/// <param name="ErrorCount">Reconnects.</param>
/// <param name="LastError">Last error.</param>
public sealed record LidarSystemInfo(string Device, bool Connected, string? Model, string? Firmware, int? Hardware,
    string? SerialNumber, string? Health, int? HealthCode, double ScanRateHz, int ErrorCount, string? LastError);

/// <summary>LCD state.</summary>
/// <param name="Line1">Line 1.</param>
/// <param name="Line2">Line 2.</param>
/// <param name="Connected">LCD working.</param>
/// <param name="Fault">LCD error.</param>
/// <param name="Address">I2C address.</param>
/// <param name="CustomMessageSecondsLeft">Seconds of custom message left.</param>
public sealed record LcdState(string Line1, string Line2, bool Connected, string? Fault, string Address, int CustomMessageSecondsLeft);

/// <summary>Response of GET /api/system.</summary>
/// <param name="System">OS, RAM, uptime, IPs.</param>
/// <param name="Lidar">LiDAR details.</param>
/// <param name="Lcd">LCD state.</param>
/// <param name="Gpio">GPIO chip in use.</param>
/// <param name="ConfigPath">Config file on the Pi.</param>
/// <param name="Simulate">Simulation mode.</param>
public sealed record SystemResponse(SystemInfo System, LidarSystemInfo Lidar, LcdState Lcd, string Gpio, string ConfigPath, bool Simulate);
