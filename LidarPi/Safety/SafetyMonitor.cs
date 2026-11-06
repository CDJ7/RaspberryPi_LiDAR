using System.Diagnostics;
using LidarPi.Config;
using LidarPi.Core;
using LidarPi.Hardware;
using LidarPi.Lidar;
using LidarPi.Services;
using Microsoft.Extensions.Options;

namespace LidarPi.Safety;

/// <summary>Everything the GUI, LCD and API need to know about the safety state at one instant.</summary>
/// <param name="TimestampUtc">When this status was computed.</param>
/// <param name="Zone">Current zone (STOP on any fault).</param>
/// <param name="ClosestM">Closest obstacle in the sector, or null if none / fault.</param>
/// <param name="BearingDeg">Bearing of that obstacle (0-360, CCW from forward).</param>
/// <param name="MotionEnabled">State of the motion-enable output.</param>
/// <param name="EStopLatched">E-stop latch state.</param>
/// <param name="EStopPressed">Debounced button state (true = pressed / wire broken).</param>
/// <param name="EStopRawPressed">Raw button state of the last sample.</param>
/// <param name="EStopReason">Why the latch is set.</param>
/// <param name="EStopLastTripUtc">Time of the last trip.</param>
/// <param name="EStopTripCount">Trips since start.</param>
/// <param name="LidarFault">True if the LiDAR data cannot be trusted (disconnected, stale, blinded).</param>
/// <param name="FaultMessage">Human-readable fault description, or null.</param>
/// <param name="ScanAgeMs">Age of the newest scan in ms (null if none yet).</param>
/// <param name="HardwareFaults">GPIO problems (LED/E-stop/motion pins), empty when all work.</param>
/// <param name="ShuttingDown">True once POST /api/shutdown was accepted.</param>
public sealed record SafetyStatus(
    DateTime TimestampUtc,
    SafetyZone Zone,
    double? ClosestM,
    double? BearingDeg,
    bool MotionEnabled,
    bool EStopLatched,
    bool EStopPressed,
    bool EStopRawPressed,
    string? EStopReason,
    DateTime? EStopLastTripUtc,
    int EStopTripCount,
    bool LidarFault,
    string? FaultMessage,
    double? ScanAgeMs,
    IReadOnlyList<string> HardwareFaults,
    bool ShuttingDown);

/// <summary>
/// The safety loop: every 10 ms it samples the E-stop, checks that LiDAR data is fresh,
/// decides the zone, drives the motion-enable output and the LEDs, and publishes a
/// <see cref="SafetyStatus"/>.
/// </summary>
/// <remarks>
/// <para><b>Fail-safe rules:</b> motion enable is HIGH only if the zone is not STOP, the
/// E-stop is not latched, there is no LiDAR fault, the E-stop input is readable and no
/// shutdown is in progress. Every unknown is treated as "stop": no scan yet, a stale scan
/// (older than 0.6 s), a blinded sensor, an unreadable E-stop pin, or an exception in this
/// loop.</para>
/// <para><b>IMPORTANT:</b> this software stop is NOT a substitute for a hardwired
/// emergency stop that cuts motor power. Software can hang, a GPIO can fail HIGH, and the
/// motor controller might ignore the signal. A real AGV needs the E-stop's second NC
/// contact to drop a safety relay / contactor in the motor supply (see WIRING.md).</para>
/// <para>WHY its own thread with a fixed period: the decision must not wait on the LiDAR's
/// serial reads, I2C writes to the LCD, or web requests. Each of those runs on its own thread
/// and talks to this loop only through <see cref="LatestValue{T}"/> holders.</para>
/// </remarks>
public sealed class SafetyMonitor : BackgroundService
{
    private readonly LidarService _lidar;
    private readonly ConfigStore _config;
    private readonly LedController _leds;
    private readonly SafetyOptions _options;
    private readonly ILogger<SafetyMonitor> _logger;
    private readonly ZoneClassifier _classifier = new();
    private readonly EmergencyStopInput _estopInput;
    private readonly DigitalOutputPin _motionEnable;
    private volatile bool _shuttingDown;
    private string? _lastLoggedFault;     // so a fault is logged when it starts/changes, not 100x per second
    private SafetyZone _lastLoggedZone = SafetyZone.Stop;

    /// <summary>Latest safety state (read by the API and the LCD thread).</summary>
    public LatestValue<SafetyStatus> Status { get; } = new();

    /// <summary>The E-stop latch (POST /api/estop/reset calls <see cref="EmergencyStopLatch.TryReset"/>).</summary>
    public EmergencyStopLatch EStop { get; }

    /// <summary>LED controller (POST /api/leds uses it).</summary>
    public LedController Leds => _leds;

    /// <summary>Creates the loop and its GPIO devices (pins are opened on the first tick).</summary>
    public SafetyMonitor(LidarService lidar, ConfigStore config, LedController leds, GpioChipLocator chip,
        SimulationControl simulation, IOptions<AppOptions> options, ILogger<SafetyMonitor> logger)
    {
        _lidar = lidar;
        _config = config;
        _leds = leds;
        _options = options.Value.Safety;
        _logger = logger;
        GpioOptions pins = options.Value.Gpio;
        _estopInput = new EmergencyStopInput(chip, pins.EStopPin, simulation, logger);
        _motionEnable = new DigitalOutputPin(chip, pins.MotionEnablePin, "motion enable", logger);
        EStop = new EmergencyStopLatch(_options.EStopTripSamples, _options.EStopReleaseStableMs, _options.RequireResetAtStartup);
    }

    /// <summary>
    /// Enters the shutdown state: motion enable LOW and LEDs off from the next tick on
    /// (called by <see cref="ShutdownService"/>).
    /// </summary>
    public void BeginShutdown()
    {
        _shuttingDown = true;
        _motionEnable.Set(false);
        _leds.AllOff();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Safety loop running every {Period} ms. NOTE: software stop only - a hardwired E-stop must cut motor power", _options.LoopPeriodMs);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(5, _options.LoopPeriodMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    // A bug must never leave motion enabled: drop the output, log, keep looping.
                    _motionEnable.Set(false);
                    LogFaultOnce($"safety loop error: {ex.Message}", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal service stop
        }
    }

    /// <summary>One pass of the safety logic (see class remarks for the rules).</summary>
    private void Tick()
    {
        DateTime now = DateTime.UtcNow;
        RuntimeConfig cfg = _config.Current;

        // 1) E-stop: raw read (fail-safe: unreadable = pressed) -> debounce -> latch.
        bool rawPressed = _estopInput.ReadPressed();
        if (EStop.Sample(rawPressed, now))
            _logger.LogWarning("E-STOP TRIPPED (trip #{Count}). Release the button and press Reset E-Stop in the GUI", EStop.TripCount);

        // 2) Is the LiDAR data trustworthy?
        var (scan, age, _) = _lidar.Scans.GetWithAge();
        string? fault = null;
        if (_shuttingDown) fault = "Shutting down";   // LiDAR is being stopped on purpose
        else if (scan is null) fault = "No LiDAR data yet" + LidarErrorSuffix();
        else if (age.TotalSeconds > _options.StaleTimeoutSeconds) fault = $"No LiDAR scan for {age.TotalSeconds:0.0} s" + LidarErrorSuffix();
        else if (scan.Points.Count < _options.MinValidPoints) fault = $"Only {scan.Points.Count} valid LiDAR points (sensor blocked?)";

        // 3) Zone (forced to STOP on a fault).
        SafetyZone zone;
        double? closest = null, bearing = null;
        if (fault is not null)
        {
            _classifier.ForceStop();
            zone = SafetyZone.Stop;
        }
        else
        {
            ScanPoint? c = scan!.ClosestInSector;
            closest = c?.RangeM;
            bearing = c is null ? null : ScanProcessor.ToBearing(c.Value.AngleDeg);
            zone = _classifier.Update(closest, cfg);
        }

        // 4) Motion enable: every condition must say "safe".
        bool estopPinOk = _estopInput.FaultMessage is null || _estopInput.IsSimulated;
        bool motion = zone != SafetyZone.Stop && !EStop.Latched && fault is null && estopPinOk && !_shuttingDown;
        _motionEnable.Set(motion);

        // 5) LEDs.
        _leds.Apply(zone, EStop.Latched, _shuttingDown, Environment.TickCount64);

        // 6) Publish for the API / LCD and log transitions.
        var hardwareFaults = _leds.Faults.ToList();
        if (_estopInput.FaultMessage is not null) hardwareFaults.Add($"E-stop input: {_estopInput.FaultMessage}");
        if (_motionEnable.FaultMessage is not null) hardwareFaults.Add($"motion enable: {_motionEnable.FaultMessage}");

        Status.Publish(new SafetyStatus(
            now, zone, closest, bearing, motion,
            EStop.Latched, EStop.Pressed, EStop.RawPressed, EStop.LatchReason, EStop.LastTripUtc, EStop.TripCount,
            fault is not null, fault,
            scan is null ? null : Math.Round(age.TotalMilliseconds),
            hardwareFaults, _shuttingDown));

        LogFaultOnce(fault, null);
        if (zone != _lastLoggedZone)
        {
            _logger.LogInformation("Zone {Old} -> {New} (closest {Dist} m)", _lastLoggedZone, zone, closest?.ToString("0.00") ?? "-");
            _lastLoggedZone = zone;
        }
    }

    /// <summary>Adds the LiDAR reader's last error to a fault message, if there is one.</summary>
    private string LidarErrorSuffix()
    {
        LidarStatus s = _lidar.Status;
        return !s.Connected && s.LastError is not null ? $" ({s.LastError})" : "";
    }

    /// <summary>Logs a fault when it appears, changes or clears - never once per tick.</summary>
    private void LogFaultOnce(string? fault, Exception? ex)
    {
        // Compare without numbers so "No scan for 0.7 s" -> "0.8 s" does not count as a new fault.
        string? key = fault is null ? null : new string(fault.Where(ch => !char.IsDigit(ch)).ToArray());
        if (key == _lastLoggedFault) return;
        if (fault is null) _logger.LogInformation("LiDAR fault cleared");
        else if (ex is not null) _logger.LogError(ex, "FAULT: {Fault}", fault);
        else _logger.LogWarning("LIDAR FAULT: {Fault}", fault);
        _lastLoggedFault = key;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // Clean shutdown (SIGTERM, systemctl stop): motion LOW first, then LEDs off.
        _motionEnable.Set(false);
        _leds.AllOff();
        _logger.LogInformation("Safety loop stopped: motion enable LOW, LEDs off");
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _motionEnable.Dispose();
        _estopInput.Dispose();
        _leds.Dispose();
        base.Dispose();
    }
}
