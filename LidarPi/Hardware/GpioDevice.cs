using System.Device.Gpio;

namespace LidarPi.Hardware;

/// <summary>
/// Abstract base for anything connected to ONE GPIO line (an LED, the motion-enable
/// output, the E-stop input).
/// </summary>
/// <remarks>
/// <para><b>Why this is a real "is-a" hierarchy:</b> an LED, the motion-enable signal and the
/// E-stop button each ARE a single GPIO line, and they share all of the awkward parts:
/// owning the pin number, opening it in the right mode on the shared controller, turning
/// a GPIO exception into a reported fault instead of a crash, retrying a failed pin no
/// more than every few seconds, logging instead of touching hardware in simulation, and
/// releasing the pin in a safe state on <see cref="Dispose"/>. Subclasses only say which
/// <see cref="PinMode"/> they need and what "safe" means for them.</para>
/// </remarks>
public abstract class GpioDevice : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    private readonly GpioChipLocator _chip;
    private DateTime _nextOpenAttemptUtc = DateTime.MinValue;
    private bool _disposed;

    /// <summary>Stores the pin assignment; the pin is opened lazily on first use.</summary>
    /// <param name="chip">Shared controller provider.</param>
    /// <param name="pin">BCM pin number.</param>
    /// <param name="name">Human-readable name used in logs and fault messages.</param>
    /// <param name="logger">Logger for state changes and faults.</param>
    protected GpioDevice(GpioChipLocator chip, int pin, string name, ILogger logger)
    {
        _chip = chip;
        Pin = pin;
        Name = name;
        Logger = logger;
    }

    /// <summary>BCM pin number.</summary>
    public int Pin { get; }

    /// <summary>Name for logs, e.g. "red LED".</summary>
    public string Name { get; }

    /// <summary>True in simulation: no hardware access, state changes are only logged.</summary>
    public bool IsSimulated => _chip.IsSimulated;

    /// <summary>Last GPIO error, or null while the pin works. Reported by the API.</summary>
    public string? FaultMessage { get; private set; }

    /// <summary>Logger for subclasses.</summary>
    protected ILogger Logger { get; }

    /// <summary>Mode the pin must be opened in (Output, InputPullUp, ...).</summary>
    protected abstract PinMode Mode { get; }

    /// <summary>Level an output pin is opened with, before any write. Outputs use Low (= off / safe).</summary>
    protected virtual PinValue InitialValue => PinValue.Low;

    /// <summary>True once the pin is open (or always in simulation).</summary>
    protected bool IsOpen { get; private set; }

    /// <summary>
    /// Opens the pin if needed. Never throws: on failure the fault is recorded and the next
    /// attempt is delayed, because this is called from the 10 ms safety loop.
    /// </summary>
    /// <returns>True if the pin can be used now.</returns>
    protected bool EnsureOpen()
    {
        if (IsOpen) return true;
        if (_disposed || DateTime.UtcNow < _nextOpenAttemptUtc) return false;
        _nextOpenAttemptUtc = DateTime.UtcNow + RetryInterval;

        if (IsSimulated)
        {
            IsOpen = true;
            Logger.LogInformation("[SIM] GPIO{Pin} ({Name}) opened as {Mode}", Pin, Name, Mode);
            return true;
        }

        GpioController? controller = _chip.TryGetController(out string? error);
        if (controller is null)
        {
            SetFault(error ?? "GPIO controller unavailable");
            return false;
        }
        try
        {
            if (!controller.IsPinOpen(Pin))
            {
                if (Mode == PinMode.Output) controller.OpenPin(Pin, Mode, InitialValue);
                else controller.OpenPin(Pin, Mode);
            }
            IsOpen = true;
            if (FaultMessage is not null) Logger.LogInformation("GPIO{Pin} ({Name}) recovered", Pin, Name);
            FaultMessage = null;
            Logger.LogInformation("GPIO{Pin} ({Name}) opened as {Mode}", Pin, Name, Mode);
            return true;
        }
        catch (Exception ex)
        {
            SetFault($"cannot open GPIO{Pin}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Writes a level. Returns false (and records a fault) if the pin is not usable.</summary>
    protected bool TryWrite(bool high)
    {
        if (!EnsureOpen()) return false;
        if (IsSimulated) return true;
        try
        {
            _chip.TryGetController(out _)!.Write(Pin, high ? PinValue.High : PinValue.Low);
            return true;
        }
        catch (Exception ex)
        {
            IsOpen = false;   // force a re-open attempt later
            SetFault($"write to GPIO{Pin} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Reads the level, or null if the pin is not usable (callers decide what is fail-safe).</summary>
    protected bool? TryRead()
    {
        if (!EnsureOpen()) return null;
        if (IsSimulated) return null;   // subclasses supply simulated values themselves
        try
        {
            return _chip.TryGetController(out _)!.Read(Pin) == PinValue.High;
        }
        catch (Exception ex)
        {
            IsOpen = false;
            SetFault($"read of GPIO{Pin} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Called just before the pin is released, so outputs can drive their safe level.</summary>
    protected virtual void OnClosing() { }

    /// <summary>Records and logs a fault, but only logs when the message changes (avoids log floods).</summary>
    private void SetFault(string message)
    {
        if (message != FaultMessage) Logger.LogError("{Name}: {Message}", Name, message);
        FaultMessage = message;
    }

    /// <summary>Puts the device in its safe state and releases the pin.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        try { OnClosing(); } catch (Exception ex) { Logger.LogDebug(ex, "Ignoring error while closing {Name}", Name); }
        if (IsOpen && !IsSimulated)
        {
            try
            {
                GpioController? controller = _chip.TryGetController(out _);
                if (controller is not null && controller.IsPinOpen(Pin)) controller.ClosePin(Pin);
            }
            catch (Exception ex) { Logger.LogDebug(ex, "Ignoring error while releasing GPIO{Pin}", Pin); }
        }
        IsOpen = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
