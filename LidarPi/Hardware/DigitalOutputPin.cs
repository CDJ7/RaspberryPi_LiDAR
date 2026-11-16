using System.Device.Gpio;

namespace LidarPi.Hardware;

/// <summary>
/// A GPIO output that remembers its requested state. Used directly for the
/// motion-enable signal, and as the base of <see cref="IndicatorLed"/>.
/// </summary>
/// <remarks>
/// Writes only when the state changes (the safety loop calls <see cref="Set"/> 100 times
/// per second), but re-writes after a GPIO fault recovers so the pin cannot silently stay
/// at an old level. On dispose it drives LOW, which for motion enable means "stop".
/// </remarks>
public class DigitalOutputPin : GpioDevice
{
    private bool? _written;   // level last written successfully; null = unknown (forces a write)

    /// <summary>Creates the output; it is opened LOW on first use.</summary>
    public DigitalOutputPin(GpioChipLocator chip, int pin, string name, ILogger logger)
        : base(chip, pin, name, logger) { }

    /// <inheritdoc/>
    protected override PinMode Mode => PinMode.Output;

    /// <summary>Level most recently requested (what the pin should be showing).</summary>
    public bool State { get; private set; }

    /// <summary>Requests a level and writes it if it differs from what the pin has.</summary>
    /// <param name="high">True = HIGH (3.3 V).</param>
    /// <param name="logChange">False suppresses the log line (used for the flashing red LED).</param>
    public void Set(bool high, bool logChange = true)
    {
        State = high;
        if (_written == high && FaultMessage is null) return;
        if (TryWrite(high))
        {
            if (logChange && _written != high)
                Logger.LogInformation("{Sim}{Name} (GPIO{Pin}) -> {Level}", IsSimulated ? "[SIM] " : "", Name, Pin, Describe(high));
            _written = high;
        }
        else
        {
            _written = null;   // unknown level: write again as soon as the pin recovers
        }
    }

    /// <summary>Text used in the log for a level ("HIGH"/"LOW"; LEDs override with "ON"/"OFF").</summary>
    protected virtual string Describe(bool high) => high ? "HIGH" : "LOW";

    /// <inheritdoc/>
    protected override void OnClosing() => Set(false);
}

/// <summary>
/// An LED wired GPIO -> 330 ohm resistor -> LED -> GND (HIGH = lit).
/// </summary>
/// <remarks>
/// It IS a digital output; the subclass only adds the colour (used by the API and GUI)
/// and logs "ON"/"OFF" so the simulation log reads naturally.
/// </remarks>
public sealed class IndicatorLed : DigitalOutputPin
{
    /// <summary>Creates the LED output.</summary>
    /// <param name="chip">Shared controller provider.</param>
    /// <param name="pin">BCM pin.</param>
    /// <param name="color">"green", "yellow" or "red".</param>
    /// <param name="logger">Logger.</param>
    public IndicatorLed(GpioChipLocator chip, int pin, string color, ILogger logger)
        : base(chip, pin, $"{color} LED", logger) => Color = color;

    /// <summary>LED colour name.</summary>
    public string Color { get; }

    /// <inheritdoc/>
    protected override string Describe(bool high) => high ? "ON" : "OFF";
}
