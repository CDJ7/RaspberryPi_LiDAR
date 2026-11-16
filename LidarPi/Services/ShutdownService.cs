using System.Diagnostics;
using LidarPi.Config;
using LidarPi.Hardware;
using LidarPi.Lidar;
using LidarPi.Safety;
using Microsoft.Extensions.Options;

namespace LidarPi.Services;

/// <summary>
/// Performs the safe shutdown requested by POST /api/shutdown.
/// </summary>
/// <remarks>
/// Order matters: (1) the HTTP response has already been sent (the endpoint starts this
/// from Response.OnCompleted), so the GUI sees "accepted" instead of a dropped connection;
/// (2) motion enable LOW and LEDs off; (3) "Shutting down..." on the LCD; (4) LiDAR scan and
/// motor stopped; (5) <c>sudo -n /sbin/shutdown -h now</c>. The sudoers rule
/// (deploy/lidar-agv.sudoers) allows exactly that one command and nothing else. "-n" makes
/// sudo fail instead of waiting for a password that nobody can type on a headless Pi.
/// In simulation the OS is NOT shut down; the program just exits, like the Pi going away.
/// </remarks>
public sealed class ShutdownService
{
    private readonly SafetyMonitor _safety;
    private readonly LcdDisplayManager _lcd;
    private readonly LidarService _lidar;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly bool _simulate;
    private readonly ILogger<ShutdownService> _logger;
    private int _started;   // 0/1 via Interlocked: a second shutdown request is ignored

    /// <summary>Creates the service.</summary>
    public ShutdownService(SafetyMonitor safety, LcdDisplayManager lcd, LidarService lidar,
        IHostApplicationLifetime lifetime, IOptions<AppOptions> options, ILogger<ShutdownService> logger)
    {
        _safety = safety;
        _lcd = lcd;
        _lidar = lidar;
        _lifetime = lifetime;
        _simulate = options.Value.Simulate;
        _logger = logger;
    }

    /// <summary>True once a shutdown was accepted.</summary>
    public bool InProgress => Volatile.Read(ref _started) == 1;

    /// <summary>Error from the shutdown command, if it failed (shown in GET /api/status).</summary>
    public string? Error { get; private set; }

    /// <summary>Starts the shutdown sequence in the background. Returns false if one is already running.</summary>
    public bool TryBegin()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return false;
        _ = Task.Run(RunAsync);
        return true;
    }

    /// <summary>The sequence from the class remarks.</summary>
    private async Task RunAsync()
    {
        try
        {
            _logger.LogWarning("Shutdown requested through the REST API");
            await Task.Delay(300);        // give Kestrel time to flush the 202 response
            _safety.BeginShutdown();      // motion enable LOW, LEDs off
            _lcd.ShowShutdown();          // "Shutting down..."
            _lidar.StopPermanently();     // stop scan + motor, do not reconnect
            await Task.Delay(500);

            if (_simulate)
            {
                _logger.LogWarning("[SIM] Would now run: sudo -n /sbin/shutdown -h now. Stopping the simulator instead");
                _lifetime.StopApplication();
                return;
            }

            using Process? p = Process.Start(new ProcessStartInfo("sudo", ["-n", "/sbin/shutdown", "-h", "now"])
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (p is null) throw new InvalidOperationException("could not start sudo");
            string stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"shutdown exited with {p.ExitCode}: {stderr.Trim()} (is deploy/lidar-agv.sudoers installed?)");
            _logger.LogWarning("OS shutdown started");
        }
        catch (Exception ex)
        {
            // Stay in the safe "shutting down" state (motion off, LiDAR stopped) and report the error.
            Error = ex.Message;
            _logger.LogError(ex, "Shutdown failed");
        }
    }
}
