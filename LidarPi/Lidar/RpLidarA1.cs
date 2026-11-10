// System.IO.Ports (NuGet "System.IO.Ports"): SerialPort, used to talk to the RPLIDAR through
// its CP2102 USB-UART adapter (/dev/rplidar -> /dev/ttyUSB0 on the Pi, COMx on Windows).
using System.Buffers.Binary;
using System.IO.Ports;
using LidarPi.Config;
using Microsoft.Extensions.Options;

namespace LidarPi.Lidar;

/// <summary>
/// Driver for the Slamtec RPLIDAR A1M8, implementing its serial protocol directly
/// (C# port of <c>apex-agv/agv_pi/lidar/rplidar_driver.py</c>; protocol reference: Slamtec
/// "RPLIDAR Interface Protocol and Application Notes", LR001).
/// </summary>
/// <remarks>
/// <para><b>Link:</b> 115200 baud, 8 data bits, no parity, 1 stop bit.</para>
/// <para><b>Motor:</b> on the A1 the scan motor is switched by the adapter's DTR line.
/// DTR asserted (<c>DtrEnable = true</c>) = motor OFF, DTR released (<c>DtrEnable = false</c>)
/// = motor ON. (Electrically the CP2102's DTR pin is active-low, which is why the senior
/// design notes call this "DTR low = motor on".) Opening the port releases DTR, so the motor
/// spins up on open; it is stopped right away and only started for scanning.</para>
/// <para><b>Requests</b> are two bytes <c>A5 cmd</c>. <b>Responses</b> start with a 7-byte
/// descriptor <c>A5 5A len[30 bits]|mode[2 bits] type</c>.</para>
/// <para><b>Scan nodes</b> are 5 bytes:
/// <code>
/// byte0: quality[7:2] | !S[1] | S[0]      S = 1 on the first node of a new revolution
/// byte1: angle_q6[6:0] &lt;&lt; 1 | C        C (check bit) is always 1
/// byte2: angle_q6[14:7]                   angle_deg = angle_q6 / 64
/// byte3-4: distance_q2 (little endian)    distance_mm = distance_q2 / 4  (0 = no return)
/// </code>
/// A node whose S and !S bits are equal, or whose C bit is 0, is corrupt: the parser then
/// slides forward ONE byte to find the node boundary again (resynchronisation).</para>
/// </remarks>
public sealed class RpLidarA1 : LidarDevice
{
    // Protocol constants (LR001).
    private const byte SyncA = 0xA5, SyncB = 0x5A;
    private const byte CmdStop = 0x25, CmdReset = 0x40, CmdScan = 0x20, CmdGetInfo = 0x50, CmdGetHealth = 0x52;
    private const byte TypeInfo = 0x04, TypeHealth = 0x06, TypeScan = 0x81;
    private const int NodeSize = 5;
    private const int MinPointsPerRevolution = 30;   // a "start" flag after fewer points is a glitch, not a revolution
    private const int MaxBadBytesPerRevolution = 2000;

    private readonly LidarOptions _options;
    private SerialPort? _port;
    private bool _scanning;
    private bool _dtrWarningLogged;   // log the "no DTR" warning once, not on every reconnect

    /// <summary>Creates the driver for the port in appsettings.json (LidarAgv:Lidar:Port).</summary>
    public RpLidarA1(IOptions<AppOptions> options, ILogger<RpLidarA1> logger) : base(logger) => _options = options.Value.Lidar;

    /// <inheritdoc/>
    public override string Description => _options.Port;

    // ------------------------------------------------------------------ connection

    /// <inheritdoc/>
    protected override void ConnectCore()
    {
        var port = new SerialPort(_options.Port, _options.Baud, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 1000,   // a running A1 sends ~2 KB/s, so 1 s of silence means motor stalled / cable pulled
            WriteTimeout = 1000,
        };
        try
        {
            port.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            // .NET reports a MISSING device as "access denied" too, so tell the two cases apart.
            if (!OperatingSystem.IsWindows() && !File.Exists(_options.Port))
                throw new LidarException($"{_options.Port} not found (LiDAR unplugged? udev rule installed? check: ls -l /dev/rplidar)", ex);
            throw new LidarException($"permission denied on {_options.Port} (add the user to the 'dialout' group, then log out/in)", ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            port.Dispose();
            throw new LidarException($"cannot open {_options.Port}: {ex.Message} (is the LiDAR plugged in? ls -l /dev/rplidar)", ex);
        }
        _port = port;
        SetMotor(false);
        port.DiscardInBuffer();
        Logger.LogInformation("Serial port {Port} opened at {Baud} baud", _options.Port, _options.Baud);
    }

    /// <summary>
    /// Closes the serial port without ever blocking the reader thread.
    /// </summary>
    /// <remarks>
    /// WHY the helper task and timeout: on Linux, .NET's SerialPort.Close()/Dispose() can block
    /// forever once the USB device has been unplugged (its internal I/O thread never finishes).
    /// The reader thread would then hang and never reconnect, which would leave the LiDAR in a
    /// permanent fault. Instead, the close runs on a helper task; if it does not finish within
    /// 1 s the old handle is abandoned (a small one-time leak per unplug) and reconnecting
    /// continues with a fresh SerialPort.
    /// </remarks>
    protected override void DisconnectCore()
    {
        SerialPort? port = _port;
        _port = null;
        _scanning = false;
        if (port is null) return;
        var close = Task.Run(() =>
        {
            try { port.Close(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { /* already broken */ }
            finally { port.Dispose(); }
        });
        if (!close.Wait(TimeSpan.FromSeconds(1)))
            Logger.LogWarning("Serial port {Port} did not close (device unplugged?); abandoning the old handle", _options.Port);
    }

    /// <inheritdoc/>
    protected override void StopScanAndMotorCore()
    {
        if (_port is not { IsOpen: true }) return;
        if (_scanning)
        {
            Send(CmdStop);
            _scanning = false;
            Thread.Sleep(10);   // the A1 needs a moment to stop streaming before the buffer is cleared
            _port.DiscardInBuffer();
        }
        SetMotor(false);
    }

    /// <inheritdoc/>
    protected override void ResetCore()
    {
        Logger.LogWarning("Sending RESET to the LiDAR");
        Send(CmdReset);
        Thread.Sleep(50);       // the core reboots; it prints a banner that we discard
        Require().DiscardInBuffer();
    }

    /// <summary>
    /// Switches the scan motor through DTR (see class remarks: DTR asserted = motor off).
    /// A port that cannot set DTR (not a CP2102, or a virtual test port) only gets a warning:
    /// if the motor then does not spin, the missing scan data is reported as a scan timeout anyway.
    /// </summary>
    private void SetMotor(bool on)
    {
        try
        {
            Require().DtrEnable = !on;
        }
        catch (IOException ex)
        {
            if (!_dtrWarningLogged) Logger.LogWarning("Cannot switch the LiDAR motor via DTR on {Port}: {Message}", _options.Port, ex.Message);
            _dtrWarningLogged = true;
        }
    }

    // ------------------------------------------------------------------ requests

    /// <inheritdoc/>
    protected override LidarDeviceInfo ReadInfoCore()
    {
        Send(CmdGetInfo);
        var (size, _, type) = ReadDescriptor();
        if (type != TypeInfo || size != 20) throw new LidarException($"bad GET_INFO descriptor (type 0x{type:X2}, size {size})");
        byte[] raw = ReadExact(20);
        // raw[0] model, raw[1] firmware minor, raw[2] firmware major, raw[3] hardware, raw[4..19] serial number
        return new LidarDeviceInfo(raw[0], $"{raw[2]}.{raw[1]:D2}", raw[3], Convert.ToHexString(raw, 4, 16));
    }

    /// <inheritdoc/>
    protected override LidarHealth ReadHealthCore()
    {
        Send(CmdGetHealth);
        var (size, _, type) = ReadDescriptor();
        if (type != TypeHealth || size != 3) throw new LidarException($"bad GET_HEALTH descriptor (type 0x{type:X2}, size {size})");
        byte[] raw = ReadExact(3);
        string status = raw[0] switch { 0 => "Good", 1 => "Warning", 2 => "Error", _ => "Unknown" };
        return new LidarHealth(status, raw[1] | (raw[2] << 8));
    }

    // ------------------------------------------------------------------ scanning

    /// <inheritdoc/>
    protected override IEnumerable<IReadOnlyList<RawMeasurement>> ScanCore(CancellationToken cancellationToken)
    {
        SerialPort port = Require();
        SetMotor(true);
        // Let the motor reach speed before asking for data (the A1 sends nothing while it is still starting).
        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(0.5, _options.MotorSpinUpSeconds)));
        port.DiscardInBuffer();
        Send(CmdScan);
        var (size, continuous, type) = ReadDescriptor();
        if (size != NodeSize || !continuous || type != TypeScan)
            throw new LidarException($"bad SCAN descriptor (type 0x{type:X2}, size {size}, continuous {continuous})");
        _scanning = true;
        Logger.LogInformation("LiDAR motor on, scanning");

        // Until the first bytes arrive, read timeouts are tolerated up to FirstDataTimeoutSeconds.
        DateTime firstDataDeadlineUtc = DateTime.UtcNow.AddSeconds(Math.Max(1.0, _options.FirstDataTimeoutSeconds));
        bool gotData = false;

        byte[] buffer = new byte[4096];
        int count = 0;                                // valid bytes in buffer
        var revolution = new List<RawMeasurement>(1600);
        int badBytes = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Host fell behind (e.g. CPU busy): drop the backlog so we never act on old data.
            if (port.BytesToRead > _options.MaxBacklogBytes)
            {
                Logger.LogWarning("LiDAR backlog {Bytes} bytes: flushing to stay real-time", port.BytesToRead);
                port.DiscardInBuffer();
                count = 0;
                revolution.Clear();
            }

            int read = ReadSome(buffer, count, allowTimeout: !gotData && DateTime.UtcNow < firstDataDeadlineUtc);
            if (read == 0) continue;   // still waiting for the first revolution after spin-up
            if (!gotData) Logger.LogInformation("LiDAR scan data flowing");
            gotData = true;
            count += read;

            int i = 0;
            while (count - i >= NodeSize)
            {
                ReadOnlySpan<byte> node = buffer.AsSpan(i, NodeSize);
                if (!TryParseNode(node, out RawMeasurement measurement, out bool startOfRevolution))
                {
                    i++;                               // resync: slide one byte and try again
                    if (++badBytes > MaxBadBytesPerRevolution) throw new LidarException("too many corrupt scan nodes (baud rate / cable?)");
                    continue;
                }
                i += NodeSize;
                if (startOfRevolution && revolution.Count > MinPointsPerRevolution)
                {
                    // Hand over this revolution and start a new list (the consumer keeps the old one).
                    yield return revolution;
                    revolution = new List<RawMeasurement>(1600);
                    badBytes = 0;
                    if (cancellationToken.IsCancellationRequested) yield break;
                }
                if (measurement.DistanceMm > 0) revolution.Add(measurement);
            }

            // Keep the incomplete tail (fewer than 5 bytes, or bytes after a resync) for the next read.
            Buffer.BlockCopy(buffer, i, buffer, 0, count - i);
            count -= i;
        }
    }

    /// <summary>
    /// Decodes one 5-byte scan node (see class remarks for the bit layout).
    /// Public and static so the protocol can be unit-tested without hardware.
    /// </summary>
    /// <param name="node">Exactly 5 bytes.</param>
    /// <param name="measurement">The decoded measurement (raw clockwise angle, mm).</param>
    /// <param name="startOfRevolution">True on the first node of a new revolution.</param>
    /// <returns>False if the check bits are invalid (corrupt or misaligned data).</returns>
    public static bool TryParseNode(ReadOnlySpan<byte> node, out RawMeasurement measurement, out bool startOfRevolution)
    {
        byte b0 = node[0], b1 = node[1];
        bool start = (b0 & 0x01) != 0;
        bool inverseStart = (b0 & 0x02) != 0;
        bool checkBit = (b1 & 0x01) != 0;
        if (start == inverseStart || !checkBit)
        {
            measurement = default;
            startOfRevolution = false;
            return false;
        }
        int angleQ6 = (b1 >> 1) | (node[2] << 7);
        int distanceQ2 = node[3] | (node[4] << 8);
        measurement = new RawMeasurement((byte)(b0 >> 2), angleQ6 / 64f, distanceQ2 / 4f);
        startOfRevolution = start;
        return true;
    }

    // ------------------------------------------------------------------ low-level I/O

    /// <summary>The open port, or an exception if the device is not connected.</summary>
    private SerialPort Require() => _port is { IsOpen: true } p ? p : throw new LidarException("LiDAR serial port is not open");

    /// <summary>Sends a two-byte request <c>A5 cmd</c>.</summary>
    private void Send(byte command)
    {
        try
        {
            Require().Write([SyncA, command], 0, 2);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            throw new LidarException($"write to LiDAR failed: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and checks the 7-byte response descriptor.</summary>
    /// <returns>Payload size, whether the response is a continuous stream, and the data type byte.</returns>
    private (int Size, bool Continuous, byte Type) ReadDescriptor()
    {
        byte[] d = ReadExact(7, "response descriptor");
        if (d[0] != SyncA || d[1] != SyncB) throw new LidarException($"bad descriptor sync {Convert.ToHexString(d)}");
        uint sizeAndMode = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(2, 4));
        return ((int)(sizeAndMode & 0x3FFFFFFF), (sizeAndMode >> 30) == 1, d[6]);
    }

    /// <summary>Reads exactly <paramref name="n"/> bytes or throws on timeout.</summary>
    private byte[] ReadExact(int n, string what = "response")
    {
        SerialPort port = Require();
        byte[] data = new byte[n];
        int got = 0;
        try
        {
            while (got < n) got += port.Read(data, got, n - got);
        }
        catch (TimeoutException ex)
        {
            throw new LidarException($"timeout reading {what} ({got}/{n} bytes) - is the LiDAR powered and on {_options.Port}?", ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new LidarException($"read from LiDAR failed: {ex.Message}", ex);
        }
        return data;
    }

    /// <summary>Reads whatever is available (at least 1 byte, waiting up to the read timeout) into the buffer tail.</summary>
    /// <param name="buffer">Receive buffer.</param>
    /// <param name="offset">Where the new bytes go.</param>
    /// <param name="allowTimeout">True during start-up: a timeout returns 0 instead of throwing.</param>
    /// <returns>Number of bytes read (0 only if <paramref name="allowTimeout"/> and nothing arrived).</returns>
    private int ReadSome(byte[] buffer, int offset, bool allowTimeout)
    {
        SerialPort port = Require();
        int space = buffer.Length - offset;
        if (space <= 0) throw new LidarException("scan buffer overflow (corrupt stream)");
        int want = Math.Clamp(port.BytesToRead, NodeSize, Math.Min(1000, space));
        try
        {
            return port.Read(buffer, offset, want);
        }
        catch (TimeoutException ex)
        {
            if (allowTimeout) return 0;
            throw new LidarException("scan data timeout (motor stalled? USB cable? weak 5 V supply?)", ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new LidarException($"read from LiDAR failed: {ex.Message}", ex);
        }
    }
}
