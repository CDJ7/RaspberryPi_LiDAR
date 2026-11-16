using System.Diagnostics;
using LidarPi.Config;
using LidarPi.Core;
using Microsoft.Extensions.Options;

namespace LidarPi.Lidar;

/// <summary>Snapshot of the LiDAR reader's state for the API and the safety loop.</summary>
/// <param name="Connected">True while revolutions are being received.</param>
/// <param name="Device">Device description ("/dev/rplidar" or "simulator").</param>
/// <param name="Info">Last GET_INFO result (kept after a disconnect).</param>
/// <param name="Health">Last GET_HEALTH result.</param>
/// <param name="ScanRateHz">Smoothed revolutions per second.</param>
/// <param name="ErrorCount">Number of failures since start (each one caused a reconnect).</param>
/// <param name="LastError">Message of the most recent failure.</param>
public sealed record LidarStatus(
    bool Connected, string Device, LidarDeviceInfo? Info, LidarHealth? Health,
    double ScanRateHz, int ErrorCount, string? LastError);

/// <summary>
/// Runs the LiDAR driver on its own thread, processes every revolution and publishes it
/// through <see cref="Scans"/>. Reconnects automatically after any error
/// (port of <c>agv_pi/lidar/lidar_service.py</c>).
/// </summary>
/// <remarks>
/// WHY a dedicated thread (not async): the serial reads block for up to the read timeout,
/// and keeping them off the thread pool means a stalled LiDAR can never delay Kestrel or
/// the safety loop. The safety loop does not depend on this thread being alive: it only
/// looks at the age of the newest scan, so a hung reader still ends in a "stale" fault.
/// </remarks>
public sealed class LidarService : BackgroundService
{
    private readonly LidarDevice _device;
    private readonly ConfigStore _config;
    private readonly LidarOptions _options;
    private readonly ILogger<LidarService> _logger;
    private readonly CancellationTokenSource _stopMotor = new();   // set by shutdown: stop and do not reconnect
    private Thread? _thread;
    private volatile LidarStatus _status;

    /// <summary>Newest processed revolution. The safety loop and GET /api/scan read from here.</summary>
    public LatestValue<ProcessedScan> Scans { get; } = new();

    /// <summary>Current reader state (cheap, lock-free read).</summary>
    public LidarStatus Status => _status;

    /// <summary>Creates the service around whichever <see cref="LidarDevice"/> Program.cs registered.</summary>
    public LidarService(LidarDevice device, ConfigStore config, IOptions<AppOptions> options, ILogger<LidarService> logger)
    {
        _device = device;
        _config = config;
        _options = options.Value.Lidar;
        _logger = logger;
        _status = new LidarStatus(false, device.Description, null, null, 0, 0, null);
    }

    /// <summary>
    /// Stops the motor and keeps it stopped (used by the shutdown command). Returns once
    /// the reader thread has closed the device, or after a 3 s timeout.
    /// </summary>
    public void StopPermanently()
    {
        _stopMotor.Cancel();
        if (_thread is not null && !_thread.Join(TimeSpan.FromSeconds(3)))
            _device.Close();   // reader is stuck in a read: close from here as a last resort
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _stopMotor.Token);
        _thread = new Thread(() => RunReader(linked.Token)) { Name = "lidar-reader", IsBackground = true };
        _thread.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        StopPermanently();   // SIGTERM / service stop: always leave the motor stopped
        _logger.LogInformation("LiDAR reader stopped");
    }

    /// <summary>Reader thread body: connect, read revolutions, and on any error wait and reconnect.</summary>
    private void RunReader(CancellationToken token)
    {
        int errors = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                _device.Open();
                _status = _status with { Connected = true, Info = _device.Info, Health = _device.Health };
                _logger.LogInformation("LiDAR connected ({Device}), scanning", _device.Description);
                double rate = 0;
                long last = Stopwatch.GetTimestamp();
                foreach (IReadOnlyList<RawMeasurement> raw in _device.ScanRevolutions(token))
                {
                    if (token.IsCancellationRequested) break;
                    long now = Stopwatch.GetTimestamp();
                    double dt = Math.Max(Stopwatch.GetElapsedTime(last, now).TotalSeconds, 1e-3);
                    last = now;
                    // Exponential moving average: smooth enough to read, quick to react.
                    rate = rate == 0 ? 1.0 / dt : 0.8 * rate + 0.2 / dt;
                    // Read the config once per revolution so a change cannot be half-applied.
                    Scans.Publish(ScanProcessor.Process(raw, _config.Current, _options));
                    _status = _status with { ScanRateHz = Math.Round(rate, 2) };
                }
            }
            catch (Exception ex)   // ANY failure (unplugged, timeout, bad data, bug) -> reconnect, never kill the thread
            {
                errors++;
                _status = _status with { Connected = false, ErrorCount = errors, LastError = ex.Message, ScanRateHz = 0 };
                _logger.LogError("LiDAR failure #{Count}: {Message}. Reconnecting in {Delay} s", errors, ex.Message, _options.ReconnectSeconds);
            }
            finally
            {
                _device.Close();
                _status = _status with { Connected = false, ScanRateHz = 0 };
            }
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(_options.ReconnectSeconds));
        }
        // Replace any old error so the fault text explains why data stopped.
        _status = _status with { Connected = false, ScanRateHz = 0, LastError = "LiDAR stopped (service stopping / shutdown)" };
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _stopMotor.Dispose();
        _device.Dispose();
        base.Dispose();
    }
}
