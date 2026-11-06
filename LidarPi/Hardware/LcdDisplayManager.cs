// System.Device.I2c (part of NuGet "System.Device.Gpio"): opens /dev/i2c-1 for the LCD backpack.
using System.Device.I2c;
using System.Globalization;
using System.Text;
// Iot.Device.CharacterLcd (NuGet "Iot.Device.Bindings"): HD44780 driver (Lcd1602) and the
// PCF8574 I2C-backpack interface (LcdInterface.CreateI2c in 4-bit mode).
using Iot.Device.CharacterLcd;
using LidarPi.Config;
using LidarPi.Lidar;
using LidarPi.Safety;
using Microsoft.Extensions.Options;

namespace LidarPi.Hardware;

/// <summary>Snapshot of the LCD for the API.</summary>
/// <param name="Line1">Text currently on line 1.</param>
/// <param name="Line2">Text currently on line 2.</param>
/// <param name="Connected">True if the LCD is initialised and working (always true in simulation).</param>
/// <param name="Fault">Last LCD error, or null.</param>
/// <param name="Address">I2C address in use, e.g. "0x27".</param>
/// <param name="CustomMessageSecondsLeft">Seconds a custom message will still be shown (0 = none).</param>
public sealed record LcdState(string Line1, string Line2, bool Connected, string? Fault, string Address, int CustomMessageSecondsLeft);

/// <summary>
/// Decides what the 16x2 HD44780 LCD shows and writes it over I2C (PCF8574 backpack
/// behind a level shifter).
/// </summary>
/// <remarks>
/// <para><b>Priority:</b> shutdown message &gt; warnings (E-stop, LiDAR fault, STOP) &gt;
/// custom message from POST /api/lcd &gt; live display. Warnings always win, so a custom
/// message can never hide a safety state. STOP counts as a warning here too, for the same reason.</para>
/// <para><b>Own thread, write only on change:</b> each character costs several I2C
/// transactions in 4-bit mode (roughly 2 ms per character), so a full redraw takes ~60 ms
/// and visibly flickers. The text is compared with what is on the screen and only changed
/// lines are rewritten, on this thread rather than the 10 ms safety loop.</para>
/// <para><b>Missing LCD:</b> init/write errors are logged once, reported by the API and
/// retried every few seconds; the rest of the service is unaffected.</para>
/// </remarks>
public sealed class LcdDisplayManager : BackgroundService
{
    private const int Columns = 16;
    private const char DegreeGlyph = (char)0xDF;   // the HD44780 A00 character ROM has a degree-like sign at 0xDF

    private readonly SafetyMonitor _safety;
    private readonly ConfigStore _config;
    private readonly LcdOptions _options;
    private readonly bool _simulate;
    private readonly ILogger<LcdDisplayManager> _logger;
    private readonly Lock _lock = new();

    private I2cDevice? _i2c;
    private Lcd1602? _lcd;
    private int _openAddress;
    private DateTime _nextInitAttemptUtc = DateTime.MinValue;
    private string? _fault;
    private string?[] _shown = new string?[2];   // what is on the glass now (null = unknown, forces a write)
    private string _customLine1 = "", _customLine2 = "";
    private DateTime _customUntilUtc = DateTime.MinValue;
    private volatile bool _showShutdown;

    /// <summary>Creates the manager. The LCD is opened on the background thread, not here.</summary>
    public LcdDisplayManager(SafetyMonitor safety, ConfigStore config, IOptions<AppOptions> options, ILogger<LcdDisplayManager> logger)
    {
        _safety = safety;
        _config = config;
        _options = options.Value.Lcd;
        _simulate = options.Value.Simulate;
        _logger = logger;
    }

    /// <summary>Shows a custom message (POST /api/lcd) for a limited time.</summary>
    /// <param name="line1">Line 1 (cut to 16 characters).</param>
    /// <param name="line2">Line 2 (cut to 16 characters).</param>
    /// <param name="seconds">Display time, 1-3600 s; the live display returns afterwards.</param>
    public void ShowCustomMessage(string line1, string line2, int seconds)
    {
        lock (_lock)
        {
            _customLine1 = Fit(line1);
            _customLine2 = Fit(line2);
            _customUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 3600));
        }
        _logger.LogInformation("LCD custom message for {Seconds} s: '{L1}' / '{L2}'", seconds, line1, line2);
    }

    /// <summary>Switches to "Shutting down..." and draws it immediately (shutdown command).</summary>
    public void ShowShutdown()
    {
        _showShutdown = true;
        Render();
    }

    /// <summary>Current LCD state for GET /api/lcd and /api/system.</summary>
    public LcdState GetState()
    {
        lock (_lock)
        {
            int left = (int)Math.Ceiling(Math.Max(0, (_customUntilUtc - DateTime.UtcNow).TotalSeconds));
            return new LcdState(_shown[0] ?? "", _shown[1] ?? "", _simulate || _lcd is not null, _fault,
                $"0x{_config.Current.LcdI2cAddress:X2}", left);
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, _options.UpdatePeriodMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) Render();
        }
        catch (OperationCanceledException)
        {
            // service stopping
        }
    }

    /// <summary>Computes the wanted text and writes the lines that changed.</summary>
    private void Render()
    {
        lock (_lock)
        {
            var (l1, l2) = ComposeLines();
            if (_simulate)
            {
                // Log at Information only when line 2 (the state text) changes; the distance on
                // line 1 changes 5x per second and would flood the journal.
                if (l1 != _shown[0] || l2 != _shown[1])
                    _logger.Log(l2 != _shown[1] ? LogLevel.Information : LogLevel.Debug,
                        "[SIM] LCD |{L1}| |{L2}|", l1.Replace(DegreeGlyph, '°'), l2);
                _shown[0] = l1;
                _shown[1] = l2;
                return;
            }
            if (!EnsureLcd()) return;
            try
            {
                WriteLineIfChanged(0, l1);
                WriteLineIfChanged(1, l2);
            }
            catch (Exception ex)   // usually IOException: cable off, wrong address, level shifter unpowered
            {
                SetFault($"LCD write failed: {ex.Message}");
                CloseLcd();
            }
        }
    }

    /// <summary>Applies the priority rules from the class remarks.</summary>
    private (string, string) ComposeLines()
    {
        if (_showShutdown) return (Fit("Shutting down..."), Fit(""));

        SafetyStatus? s = _safety.Status.Get();
        if (s is null) return (Fit("LiDAR AGV"), Fit("Starting..."));

        string live = s.ClosestM is double d && s.BearingDeg is double b
            ? string.Create(CultureInfo.InvariantCulture, $"Near: {d:0.00}m {Math.Round(b) % 360:000}{DegreeGlyph}")
            : s.LidarFault ? "Near: --.--m" : "Near: none";

        if (s.EStopLatched) return (Fit(live), Fit("!! E-STOP !!"));
        if (s.LidarFault) return (Fit(live), Fit("LIDAR FAULT"));
        if (s.Zone == SafetyZone.Stop) return (Fit(live), Fit("STOP-TOO CLOSE"));
        if (DateTime.UtcNow < _customUntilUtc) return (_customLine1, _customLine2);
        return (Fit(live), Fit(s.Zone == SafetyZone.Caution ? "CAUTION - CLOSE" : "CLEAR"));
    }

    /// <summary>Opens the LCD if needed; re-opens it when the configured address changed.</summary>
    private bool EnsureLcd()
    {
        int address = _config.Current.LcdI2cAddress;
        if (_lcd is not null && address == _openAddress) return true;
        if (_lcd is not null) { _logger.LogInformation("LCD address changed to 0x{Addr:X2}", address); CloseLcd(); _nextInitAttemptUtc = DateTime.MinValue; }
        if (DateTime.UtcNow < _nextInitAttemptUtc) return false;
        _nextInitAttemptUtc = DateTime.UtcNow.AddSeconds(_options.RetrySeconds);
        try
        {
            _i2c = I2cDevice.Create(new I2cConnectionSettings(_options.I2cBus, address));
            // false = 4-bit mode: the PCF8574 backpack only wires D4-D7 of the HD44780.
            _lcd = new Lcd1602(LcdInterface.CreateI2c(_i2c, false)) { BacklightOn = true, DisplayOn = true };
            _lcd.Clear();
            _openAddress = address;
            _shown = new string?[2];
            if (_fault is not null) _logger.LogInformation("LCD recovered");
            _fault = null;
            _logger.LogInformation("LCD opened on /dev/i2c-{Bus} at 0x{Addr:X2}", _options.I2cBus, address);
            return true;
        }
        catch (Exception ex)
        {
            SetFault($"LCD not found at 0x{address:X2} on i2c-{_options.I2cBus}: {ex.Message} (check wiring and i2cdetect -y {_options.I2cBus})");
            CloseLcd();
            return false;
        }
    }

    /// <summary>Rewrites one line only if its text differs from what is displayed.</summary>
    private void WriteLineIfChanged(int row, string text)
    {
        if (_shown[row] == text) return;
        _lcd!.SetCursorPosition(0, row);
        _lcd.Write(text);   // always 16 characters, so old text is overwritten without a Clear() flash
        _shown[row] = text;
    }

    /// <summary>Pads/cuts text to exactly 16 characters and replaces anything the LCD ROM cannot show.</summary>
    private static string Fit(string text)
    {
        var sb = new StringBuilder(Columns);
        foreach (char ch in text)
        {
            if (sb.Length == Columns) break;
            sb.Append(ch == DegreeGlyph || ch == '°' ? DegreeGlyph : ch is >= ' ' and <= '}' ? ch : '?');
        }
        return sb.ToString().PadRight(Columns);
    }

    /// <summary>Records an LCD error and logs it once.</summary>
    private void SetFault(string message)
    {
        if (message != _fault) _logger.LogError("{Message}", message);
        _fault = message;
    }

    /// <summary>Releases the LCD objects (the next Render() retries).</summary>
    private void CloseLcd()
    {
        try { _lcd?.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Ignoring LCD dispose error"); }
        try { _i2c?.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Ignoring I2C dispose error"); }
        _lcd = null;
        _i2c = null;
        _shown = new string?[2];
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        lock (_lock)
        {
            // Leave "Shutting down..." visible after a shutdown command; otherwise clear the screen.
            if (!_showShutdown && _lcd is not null)
            {
                try { _lcd.Clear(); _lcd.BacklightOn = false; }
                catch (Exception ex) { _logger.LogDebug(ex, "Ignoring LCD error during stop"); }
            }
            if (_simulate && !_showShutdown) _logger.LogInformation("[SIM] LCD cleared");
            CloseLcd();
        }
    }
}
