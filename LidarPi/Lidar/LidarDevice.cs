namespace LidarPi.Lidar;

/// <summary>Any LiDAR communication, protocol or health failure. <see cref="LidarService"/> reconnects on it.</summary>
public sealed class LidarException : Exception
{
    /// <summary>Creates the exception with a message for the log / API.</summary>
    public LidarException(string message) : base(message) { }

    /// <summary>Creates the exception wrapping a lower-level error (serial port, I/O).</summary>
    public LidarException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Abstract base for a 2D scanning LiDAR that delivers one revolution at a time.
/// </summary>
/// <remarks>
/// <para><b>Why this is a real "is-a" hierarchy:</b> <see cref="LidarService"/> and the
/// <c>--check</c> tool only need "a LiDAR": open it, read its info and health, get
/// revolutions, close it. <see cref="RpLidarA1"/> (real serial device) and
/// <see cref="SimulatedLidar"/> (fake moving scene for <c>--simulate</c>) both ARE such a
/// LiDAR, so the rest of the program runs unchanged against either one, with no
/// <c>if (simulate)</c> branches. This mirrors the <c>ScanSource</c> protocol of the
/// senior-design Python code.</para>
/// <para><b>Behaviour reused from the base:</b> the open sequence (connect, read info,
/// check health, reset on "Error"), the cached <see cref="Info"/>/<see cref="Health"/>
/// shown by the API, best-effort closing that always tries to stop the motor, and
/// <see cref="IDisposable"/>. Subclasses only implement the device-specific "Core" steps
/// (template-method pattern).</para>
/// </remarks>
public abstract class LidarDevice : IDisposable
{
    /// <summary>Creates the device wrapper; nothing is opened until <see cref="Open"/>.</summary>
    protected LidarDevice(ILogger logger) => Logger = logger;

    /// <summary>Logger shared with subclasses so all LiDAR messages use one category.</summary>
    protected ILogger Logger { get; }

    /// <summary>Short description for logs and the API, e.g. "/dev/rplidar" or "simulator".</summary>
    public abstract string Description { get; }

    /// <summary>True between a successful <see cref="Open"/> and <see cref="Close"/>.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Device info read during the last successful <see cref="Open"/>, kept after disconnect for the API.</summary>
    public LidarDeviceInfo? Info { get; private set; }

    /// <summary>Health read during the last <see cref="Open"/>.</summary>
    public LidarHealth? Health { get; private set; }

    /// <summary>
    /// Connects, reads device info and health. Throws <see cref="LidarException"/> on any
    /// failure (the caller retries). A health "Error" triggers a device reset first, as the
    /// Slamtec datasheet recommends.
    /// </summary>
    public void Open()
    {
        Close();
        ConnectCore();
        IsConnected = true;
        try
        {
            Info = ReadInfoCore();
            Health = ReadHealthCore();
            Logger.LogInformation("LiDAR {Desc}: {Model}, firmware {Fw}, hardware {Hw}, serial {Serial}, health {Health} (code {Code})",
                Description, Info.ModelName, Info.Firmware, Info.Hardware, Info.SerialNumber, Health.Status, Health.ErrorCode);
            if (!Info.IsA1Series)
                Logger.LogWarning("Model byte 0x{Model:X2} is not an A1: motor control by DTR may not work on this unit", Info.Model);
            if (Health.Status == "Error")
            {
                ResetCore();
                throw new LidarException($"LiDAR health error code {Health.ErrorCode} (reset issued)");
            }
        }
        catch
        {
            Close();
            throw;
        }
    }

    /// <summary>
    /// Starts the motor and scanning and yields one list of measurements per full revolution.
    /// Runs until cancelled or until a <see cref="LidarException"/> is thrown.
    /// </summary>
    public IEnumerable<IReadOnlyList<RawMeasurement>> ScanRevolutions(CancellationToken cancellationToken)
    {
        if (!IsConnected) throw new LidarException("LiDAR is not connected");
        return ScanCore(cancellationToken);
    }

    /// <summary>
    /// Best-effort stop and disconnect: always tries to stop scanning and the motor, and
    /// never throws, because it is called from error handlers and shutdown paths.
    /// </summary>
    public void Close()
    {
        if (!IsConnected) return;
        try { StopScanAndMotorCore(); }
        catch (Exception ex) { Logger.LogDebug(ex, "Ignoring error while stopping LiDAR"); }
        try { DisconnectCore(); }
        catch (Exception ex) { Logger.LogDebug(ex, "Ignoring error while disconnecting LiDAR"); }
        IsConnected = false;
    }

    /// <summary>Same as <see cref="Close"/>; lets the device be used in a <c>using</c> block.</summary>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens the transport (serial port, simulator state). Throw <see cref="LidarException"/> on failure.</summary>
    protected abstract void ConnectCore();

    /// <summary>Closes the transport.</summary>
    protected abstract void DisconnectCore();

    /// <summary>Requests device information (RPLIDAR GET_INFO).</summary>
    protected abstract LidarDeviceInfo ReadInfoCore();

    /// <summary>Requests device health (RPLIDAR GET_HEALTH).</summary>
    protected abstract LidarHealth ReadHealthCore();

    /// <summary>Device-specific scan loop; see <see cref="ScanRevolutions"/>.</summary>
    protected abstract IEnumerable<IReadOnlyList<RawMeasurement>> ScanCore(CancellationToken cancellationToken);

    /// <summary>Stops scanning and spins the motor down.</summary>
    protected abstract void StopScanAndMotorCore();

    /// <summary>Soft-resets the device after a health error. Default: nothing to reset.</summary>
    protected virtual void ResetCore() { }
}
