using LidarPi.Hardware;
using LidarPi.Lidar;
using LidarPi.Services;

namespace LidarPi.Api;

// Data transfer objects of the REST API. They are kept separate from the internal types
// so the JSON contract (mirrored in LidarPC/Api/ApiDtos.cs) stays stable while the
// internals change. JSON uses camelCase property names and enums as strings.

/// <summary>Error body returned with every 4xx/5xx response: <c>{ "error": "..." }</c>.</summary>
/// <param name="Error">Human-readable message for the GUI.</param>
public sealed record ApiError(string Error);

/// <summary>Generic success body: <c>{ "message": "..." }</c>.</summary>
/// <param name="Message">What happened.</param>
public sealed record ApiMessage(string Message);

/// <summary>Body of POST /api/leds.</summary>
/// <param name="Mode">"auto" or "test".</param>
/// <param name="Green">Test mode: green on.</param>
/// <param name="Yellow">Test mode: yellow on.</param>
/// <param name="Red">Test mode: red on.</param>
/// <param name="TimeoutSeconds">Test mode: seconds before auto mode resumes (default 30).</param>
public sealed record LedRequest(string Mode, bool Green = false, bool Yellow = false, bool Red = false, int TimeoutSeconds = 30);

/// <summary>LED state returned by POST/GET /api/leds and inside the status.</summary>
/// <param name="Mode">Auto or Test.</param>
/// <param name="Green">Green LED lit.</param>
/// <param name="Yellow">Yellow LED lit.</param>
/// <param name="Red">Red LED lit.</param>
/// <param name="TestSecondsRemaining">Seconds until test mode ends.</param>
public sealed record LedState(LedMode Mode, bool Green, bool Yellow, bool Red, int TestSecondsRemaining);

/// <summary>Body of POST /api/lcd.</summary>
/// <param name="Line1">Line 1 text (max 16 characters shown).</param>
/// <param name="Line2">Line 2 text.</param>
/// <param name="Seconds">How long to show it (1-3600).</param>
public sealed record LcdRequest(string? Line1, string? Line2, int Seconds = 10);

/// <summary>Response of GET /api/estop (the "observe an input" requirement).</summary>
/// <param name="Pressed">Debounced button state: true = pressed or wire broken.</param>
/// <param name="RawPressed">Instantaneous raw reading.</param>
/// <param name="RawLevel">Raw pin level: "HIGH" (open contact = STOP) or "LOW" (closed contact = released).</param>
/// <param name="Latched">True until released AND reset.</param>
/// <param name="Reason">Why it is latched.</param>
/// <param name="LastTripUtc">Time of the last trip.</param>
/// <param name="TripCount">Trips since the service started.</param>
/// <param name="InputFault">GPIO error on the E-stop pin, if any.</param>
public sealed record EStopState(bool Pressed, bool RawPressed, string RawLevel, bool Latched, string? Reason,
    DateTime? LastTripUtc, int TripCount, string? InputFault);

/// <summary>Body of PUT /api/config. Every field is optional; only given fields change.</summary>
/// <param name="StopDistanceM">STOP threshold in metres.</param>
/// <param name="CautionDistanceM">CAUTION threshold in metres.</param>
/// <param name="HysteresisM">Hysteresis band in metres.</param>
/// <param name="SectorMode">"Full" or "Front".</param>
/// <param name="FrontHalfAngleDeg">Front sector half-angle.</param>
/// <param name="AngleOffsetDeg">LiDAR mounting offset.</param>
/// <param name="ClusterGapM">Object clustering gap.</param>
/// <param name="MinClusterPoints">Minimum points per object.</param>
/// <param name="LcdI2cAddress">LCD I2C address (decimal, e.g. 39 for 0x27).</param>
public sealed record ConfigUpdateRequest(
    double? StopDistanceM, double? CautionDistanceM, double? HysteresisM, string? SectorMode,
    double? FrontHalfAngleDeg, double? AngleOffsetDeg, double? ClusterGapM, int? MinClusterPoints, int? LcdI2cAddress);

/// <summary>Response of GET/PUT /api/config (never includes the password hash).</summary>
/// <param name="StopDistanceM">STOP threshold.</param>
/// <param name="CautionDistanceM">CAUTION threshold.</param>
/// <param name="HysteresisM">Hysteresis band.</param>
/// <param name="SectorMode">"Full" or "Front".</param>
/// <param name="FrontHalfAngleDeg">Front sector half-angle.</param>
/// <param name="AngleOffsetDeg">LiDAR mounting offset.</param>
/// <param name="ClusterGapM">Clustering gap.</param>
/// <param name="MinClusterPoints">Minimum points per object.</param>
/// <param name="LcdI2cAddress">LCD address (decimal).</param>
/// <param name="Username">Current API user name.</param>
/// <param name="IsDefaultPassword">True while the default password is still in use.</param>
/// <param name="ConfigPath">Where the file is stored on the Pi.</param>
public sealed record ConfigResponse(
    double StopDistanceM, double CautionDistanceM, double HysteresisM, string SectorMode, double FrontHalfAngleDeg,
    double AngleOffsetDeg, double ClusterGapM, int MinClusterPoints, int LcdI2cAddress, string Username,
    bool IsDefaultPassword, string ConfigPath);

/// <summary>Body of PUT /api/config/password.</summary>
/// <param name="CurrentPassword">Must match the stored password.</param>
/// <param name="NewPassword">At least 6 characters.</param>
/// <param name="NewUsername">Optional new user name.</param>
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword, string? NewUsername);

/// <summary>Body of POST /api/shutdown. <c>confirm</c> must be true, so a stray empty POST cannot power off the Pi.</summary>
/// <param name="Confirm">Must be true.</param>
public sealed record ShutdownRequest(bool Confirm);

/// <summary>Body of POST /api/sim (simulation only).</summary>
/// <param name="EStopPressed">Press/release the simulated E-stop button.</param>
/// <param name="LidarUnplugged">Unplug/replug the simulated LiDAR.</param>
public sealed record SimRequest(bool? EStopPressed, bool? LidarUnplugged);

/// <summary>Compact live status (GET /api/status, and embedded in GET /api/scan).</summary>
/// <param name="TimestampUtc">When the safety loop computed it.</param>
/// <param name="Zone">Clear, Caution or Stop.</param>
/// <param name="ClosestM">Closest obstacle in the sector.</param>
/// <param name="BearingDeg">Its bearing (0-360 CCW from forward).</param>
/// <param name="MotionEnabled">Motion-enable output state.</param>
/// <param name="EStopLatched">E-stop latch.</param>
/// <param name="EStopPressed">Debounced button state.</param>
/// <param name="EStopReason">Why it is latched.</param>
/// <param name="LidarFault">True if the LiDAR data is not trustworthy.</param>
/// <param name="FaultMessage">Fault description.</param>
/// <param name="HardwareFaults">GPIO/LCD problems.</param>
/// <param name="Leds">LED state.</param>
/// <param name="LcdLine1">What the LCD shows on line 1.</param>
/// <param name="LcdLine2">What the LCD shows on line 2.</param>
/// <param name="ShuttingDown">Shutdown in progress.</param>
/// <param name="ShutdownError">Error if the shutdown command failed.</param>
/// <param name="Simulate">True when the Pi app runs with --simulate.</param>
/// <param name="DefaultPassword">True while the default password is in use (GUI shows a warning).</param>
public sealed record StatusResponse(
    DateTime TimestampUtc, SafetyZone Zone, double? ClosestM, double? BearingDeg, bool MotionEnabled,
    bool EStopLatched, bool EStopPressed, string? EStopReason, bool LidarFault, string? FaultMessage,
    IReadOnlyList<string> HardwareFaults, LedState Leds, string LcdLine1, string LcdLine2,
    bool ShuttingDown, string? ShutdownError, bool Simulate, bool DefaultPassword);

/// <summary>Response of GET /api/scan: what the GUI plots ~10 times per second.</summary>
/// <param name="Status">Live status (zone, E-stop, fault...).</param>
/// <param name="ScanTimestampUtc">When the revolution was received (null if none yet).</param>
/// <param name="Points">Downsampled points as [x, y] in metres (robot frame: x forward, y left).</param>
/// <param name="Objects">Detected objects, closest first.</param>
/// <param name="StopDistanceM">Current STOP threshold (for the red circle).</param>
/// <param name="CautionDistanceM">Current CAUTION threshold (for the yellow circle).</param>
/// <param name="SectorMode">"Full" or "Front".</param>
/// <param name="FrontHalfAngleDeg">Front sector half-angle (for drawing the sector).</param>
/// <param name="ScanRateHz">LiDAR revolutions per second.</param>
public sealed record ScanResponse(
    StatusResponse Status, DateTime? ScanTimestampUtc, IReadOnlyList<float[]> Points, IReadOnlyList<DetectedObject> Objects,
    double StopDistanceM, double CautionDistanceM, string SectorMode, double FrontHalfAngleDeg, double ScanRateHz);

/// <summary>LiDAR details inside GET /api/system.</summary>
/// <param name="Device">Serial device or "simulator".</param>
/// <param name="Connected">Receiving data.</param>
/// <param name="Model">Model name from GET_INFO.</param>
/// <param name="Firmware">Firmware version.</param>
/// <param name="Hardware">Hardware revision.</param>
/// <param name="SerialNumber">Serial number.</param>
/// <param name="Health">Good / Warning / Error.</param>
/// <param name="HealthCode">Health error code.</param>
/// <param name="ScanRateHz">Revolutions per second.</param>
/// <param name="ErrorCount">Reconnects since start.</param>
/// <param name="LastError">Last LiDAR error.</param>
public sealed record LidarSystemInfo(string Device, bool Connected, string? Model, string? Firmware, int? Hardware,
    string? SerialNumber, string? Health, int? HealthCode, double ScanRateHz, int ErrorCount, string? LastError);

/// <summary>Response of GET /api/system.</summary>
/// <param name="System">OS version, RAM, uptime, IP addresses.</param>
/// <param name="Lidar">LiDAR model, firmware, health, scan rate.</param>
/// <param name="Lcd">LCD state.</param>
/// <param name="Gpio">GPIO chip/driver in use.</param>
/// <param name="ConfigPath">Location of the nonvolatile config.</param>
/// <param name="Simulate">Running with --simulate.</param>
public sealed record SystemResponse(SystemInfo System, LidarSystemInfo Lidar, LcdState Lcd, string Gpio, string ConfigPath, bool Simulate);
