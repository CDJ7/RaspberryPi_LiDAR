using LidarPi.Config;
using LidarPi.Lidar;
using Microsoft.Extensions.Options;

namespace LidarPi.Hardware;

/// <summary>LED operating mode.</summary>
public enum LedMode
{
    /// <summary>LEDs follow the safety state (normal operation).</summary>
    Auto,
    /// <summary>LEDs are set by hand through POST /api/leds until the timeout or "auto".</summary>
    Test,
}

/// <summary>
/// Drives the green / yellow / red LEDs, either from the safety state (auto) or from a
/// manual pattern (test mode, the course's "REST command that changes an output").
/// </summary>
/// <remarks>
/// Auto mode mapping: CLEAR = green, CAUTION = yellow, STOP = red, LiDAR fault = red,
/// E-stop = red flashing (so an E-stop is distinguishable from a normal stop at a glance).
/// Test mode only affects the LEDs: the motion-enable output is NEVER controlled from
/// here, so an LED test can never enable motion. Test mode always times out back to
/// auto, so the indicators cannot be left showing a wrong state forever.
/// </remarks>
public sealed class LedController : IDisposable
{
    private readonly Lock _lock = new();   // API thread (SetTest/SetAuto) vs safety thread (Apply)
    private readonly IndicatorLed _green, _yellow, _red;
    private readonly SafetyOptions _safety;
    private readonly ILogger<LedController> _logger;
    private bool _testGreen, _testYellow, _testRed;
    private DateTime _testUntilUtc;
    private bool _flashing;   // remembers that red is flashing so the change is logged once, not every toggle

    /// <summary>Creates the three LED outputs from the configured pins.</summary>
    public LedController(GpioChipLocator chip, IOptions<AppOptions> options, ILogger<LedController> logger)
    {
        GpioOptions pins = options.Value.Gpio;
        _safety = options.Value.Safety;
        _logger = logger;
        _green = new IndicatorLed(chip, pins.GreenLedPin, "green", logger);
        _yellow = new IndicatorLed(chip, pins.YellowLedPin, "yellow", logger);
        _red = new IndicatorLed(chip, pins.RedLedPin, "red", logger);
    }

    /// <summary>Current mode.</summary>
    public LedMode Mode { get; private set; } = LedMode.Auto;

    /// <summary>Seconds until test mode returns to auto (0 in auto mode).</summary>
    public int TestSecondsRemaining
    {
        get
        {
            lock (_lock)
            {
                return Mode == LedMode.Test ? (int)Math.Ceiling(Math.Max(0, (_testUntilUtc - DateTime.UtcNow).TotalSeconds)) : 0;
            }
        }
    }

    /// <summary>What each LED is showing right now (green, yellow, red).</summary>
    public (bool Green, bool Yellow, bool Red) States => (_green.State, _yellow.State, _red.State);

    /// <summary>Faults of any LED pin (empty when all work).</summary>
    public IEnumerable<string> Faults =>
        new[] { _green, _yellow, _red }.Where(l => l.FaultMessage is not null).Select(l => $"{l.Name}: {l.FaultMessage}");

    /// <summary>Switches to test mode with a manual pattern.</summary>
    /// <param name="green">Green on/off.</param>
    /// <param name="yellow">Yellow on/off.</param>
    /// <param name="red">Red on/off.</param>
    /// <param name="seconds">How long before auto mode resumes (clamped to 1..LedTestMaxSeconds).</param>
    /// <returns>The timeout actually used.</returns>
    public int SetTest(bool green, bool yellow, bool red, int seconds)
    {
        int clamped = Math.Clamp(seconds, 1, _safety.LedTestMaxSeconds);
        lock (_lock)
        {
            _testGreen = green;
            _testYellow = yellow;
            _testRed = red;
            _testUntilUtc = DateTime.UtcNow.AddSeconds(clamped);
            Mode = LedMode.Test;
        }
        _logger.LogInformation("LED test mode: green={G} yellow={Y} red={R} for {S} s", green, yellow, red, clamped);
        return clamped;
    }

    /// <summary>Returns to auto mode immediately.</summary>
    public void SetAuto()
    {
        lock (_lock) { Mode = LedMode.Auto; }
        _logger.LogInformation("LED auto mode");
    }

    /// <summary>
    /// Updates the LEDs; called every safety-loop tick.
    /// </summary>
    /// <param name="zone">Current zone (already STOP if there is a fault).</param>
    /// <param name="estopLatched">E-stop latched: red flashes.</param>
    /// <param name="shuttingDown">Shutdown in progress: all LEDs off.</param>
    /// <param name="nowMs">Monotonic milliseconds, used for the flash phase.</param>
    public void Apply(SafetyZone zone, bool estopLatched, bool shuttingDown, long nowMs)
    {
        bool g, y, r;
        bool flash = false;
        lock (_lock)
        {
            if (Mode == LedMode.Test && DateTime.UtcNow >= _testUntilUtc)
            {
                Mode = LedMode.Auto;
                _logger.LogInformation("LED test mode timed out; back to auto");
            }

            if (shuttingDown) { g = y = r = false; }
            else if (Mode == LedMode.Test) { g = _testGreen; y = _testYellow; r = _testRed; }
            else if (estopLatched)
            {
                flash = true;
                g = y = false;
                r = (nowMs / Math.Max(50, _safety.FlashHalfPeriodMs)) % 2 == 0;
            }
            else
            {
                g = zone == SafetyZone.Clear;
                y = zone == SafetyZone.Caution;
                r = zone == SafetyZone.Stop;
            }
        }

        if (flash != _flashing)
        {
            _flashing = flash;
            _logger.LogInformation("{Sim}red LED {State}", _red.IsSimulated ? "[SIM] " : "", flash ? "FLASHING (E-stop)" : "steady");
        }
        _green.Set(g);
        _yellow.Set(y);
        _red.Set(r, logChange: !flash);
    }

    /// <summary>Turns every LED off (clean shutdown).</summary>
    public void AllOff()
    {
        _green.Set(false);
        _yellow.Set(false);
        _red.Set(false);
    }

    /// <summary>Turns the LEDs off and releases their pins.</summary>
    public void Dispose()
    {
        _green.Dispose();
        _yellow.Dispose();
        _red.Dispose();
    }
}
