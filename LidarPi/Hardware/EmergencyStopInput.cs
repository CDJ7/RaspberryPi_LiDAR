using System.Device.Gpio;
using LidarPi.Services;

namespace LidarPi.Hardware;

/// <summary>
/// The E-stop button input: a latching mushroom switch whose NORMALLY CLOSED contact
/// connects the GPIO to GND, with the Pi's internal pull-up enabled.
/// </summary>
/// <remarks>
/// Fail-safe wiring: released = contact closed = pin pulled to GND = LOW.
/// Pressed OR a broken/unplugged wire = contact open = pull-up wins = HIGH = STOP.
/// A failed GPIO read is also reported as "pressed", so every failure mode stops motion.
/// Debouncing and latching are done by <see cref="Safety.EmergencyStopLatch"/>; this
/// class only reports the raw level.
/// </remarks>
public sealed class EmergencyStopInput : GpioDevice
{
    private readonly SimulationControl _simulation;

    /// <summary>Creates the input; opened with the internal pull-up on first read.</summary>
    public EmergencyStopInput(GpioChipLocator chip, int pin, SimulationControl simulation, ILogger logger)
        : base(chip, pin, "E-stop input", logger) => _simulation = simulation;

    /// <inheritdoc/>
    protected override PinMode Mode => PinMode.InputPullUp;

    /// <summary>
    /// Reads the button. True = pressed, wire broken, or GPIO unreadable (all mean STOP).
    /// In simulation the value comes from <see cref="SimulationControl.EStopPressed"/>.
    /// </summary>
    public bool ReadPressed()
    {
        if (IsSimulated) return _simulation.EStopPressed;
        bool? high = TryRead();
        return high ?? true;   // unknown = pressed (fail-safe)
    }
}
