// System.Device.Gpio (NuGet "System.Device.Gpio"): .NET IoT GPIO API. Used here with the
// libgpiod character-device driver, which is the only supported way to drive GPIO on the
// Raspberry Pi 5 (its pins sit on the RP1 chip; RPi.GPIO-style register access does not work).
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using LidarPi.Config;
using Microsoft.Extensions.Options;

namespace LidarPi.Hardware;

/// <summary>
/// Creates and owns the single <see cref="GpioController"/> shared by every GPIO device,
/// choosing the right gpiochip for the Pi 5.
/// </summary>
/// <remarks>
/// WHY auto-detect: the Pi 5's header pins are on the RP1 chip, labelled "pinctrl-rp1".
/// Older Pi 5 kernels number it gpiochip4, newer ones gpiochip0, so a hard-coded number
/// breaks after an OS update. If creating the controller fails (libgpiod missing, no
/// permission) the error is remembered and retried later instead of crashing the service.
/// In simulation no controller is created at all and every device only logs.
/// </remarks>
public sealed class GpioChipLocator : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly GpioOptions _options;
    private readonly ILogger<GpioChipLocator> _logger;
    private readonly object _lock = new();
    private GpioController? _controller;
    private DateTime _nextAttemptUtc = DateTime.MinValue;

    /// <summary>Creates the locator; the controller is opened lazily on first use.</summary>
    public GpioChipLocator(IOptions<AppOptions> options, ILogger<GpioChipLocator> logger)
    {
        _options = options.Value.Gpio;
        IsSimulated = options.Value.Simulate;
        _logger = logger;
    }

    /// <summary>True with <c>--simulate</c>: devices log instead of touching pins.</summary>
    public bool IsSimulated { get; }

    /// <summary>Which chip and driver are in use (shown by GET /api/system), or the last error.</summary>
    public string Description { get; private set; } = "not opened yet";

    /// <summary>
    /// Returns the shared controller, creating it on first use. Returns null (with the reason
    /// in <paramref name="error"/>) if GPIO is unavailable; callers then report a fault.
    /// </summary>
    public GpioController? TryGetController(out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (_controller is not null) return _controller;
            if (IsSimulated) { error = "simulation"; return null; }
            if (DateTime.UtcNow < _nextAttemptUtc) { error = Description; return null; }
            _nextAttemptUtc = DateTime.UtcNow + RetryInterval;
            try
            {
                _controller = Create();
                return _controller;
            }
            catch (Exception ex)
            {
                Description = $"GPIO unavailable: {ex.Message}";
                error = Description;
                _logger.LogError("Could not open GPIO ({Message}). Retrying every {Seconds} s", ex.Message, RetryInterval.TotalSeconds);
                return null;
            }
        }
    }

    /// <summary>Finds the chip and opens it, preferring the libgpiod v1 driver (Bookworm ships libgpiod 1.6).</summary>
    private GpioController Create()
    {
        Exception? v1Error = null;
        try
        {
            int chip = SelectChip(LibGpiodDriver.GetAvailableChips());
            var controller = new GpioController(new LibGpiodDriver(chip));
            Description = $"gpiochip{chip} via libgpiod v1";
            _logger.LogInformation("GPIO opened: {Description}", Description);
            return controller;
        }
        catch (Exception ex)
        {
            v1Error = ex;   // newer OS images (libgpiod 2.x) only work with the v2 driver
        }
        try
        {
            int chip = SelectChip(LibGpiodV2Driver.GetAvailableChips());
            var controller = new GpioController(new LibGpiodV2Driver(chip));
            Description = $"gpiochip{chip} via libgpiod v2";
            _logger.LogInformation("GPIO opened: {Description}", Description);
            return controller;
        }
        catch (Exception v2Error)
        {
            throw new InvalidOperationException($"libgpiod v1: {v1Error.Message}; libgpiod v2: {v2Error.Message}", v2Error);
        }
    }

    /// <summary>Applies the "auto" rule or the configured chip number.</summary>
    private int SelectChip(IList<GpioChipInfo> chips)
    {
        foreach (GpioChipInfo c in chips) _logger.LogInformation("Found {Chip}", c);
        if (!string.Equals(_options.Chip, "auto", StringComparison.OrdinalIgnoreCase))
            return int.Parse(_options.Chip, System.Globalization.CultureInfo.InvariantCulture);
        GpioChipInfo? rp1 = chips.FirstOrDefault(c => c.Label.Contains("rp1", StringComparison.OrdinalIgnoreCase));
        if (rp1 is not null) return rp1.Id;
        // Not a Pi 5 (or a very unusual kernel): chip 0 is the header GPIO on older Pis.
        _logger.LogWarning("No 'pinctrl-rp1' gpiochip found; falling back to gpiochip0. Set LidarAgv:Gpio:Chip if this is wrong");
        return 0;
    }

    /// <summary>Releases the controller (and with it all pins still open).</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _controller?.Dispose();
            _controller = null;
        }
    }
}
