using System.Diagnostics;
using System.Globalization;
using LidarPi.Config;
using LidarPi.Lidar;
using Microsoft.Extensions.Options;

namespace LidarPi.Tools;

/// <summary>
/// The <c>--check</c> mode: a LiDAR smoke test that prints device info, health, scan rate
/// and the closest distance to the front / left / back / right (C# port of
/// <c>apex-agv/agv_pi/tools/lidar_check.py</c>).
/// </summary>
/// <remarks>
/// <para><b>Orientation check:</b> put an object about 0.5 m to the robot's LEFT. The
/// "left" column must show about 0.50 m. If it appears under "right", the clockwise/
/// counter-clockwise conversion is wrong for this mounting (LidarAgv:Lidar:Clockwise); if
/// it appears under "front" or "back", the LiDAR is rotated, so set the angle offset
/// (Configuration tab, "LiDAR angle offset") until it lines up.</para>
/// <para>Stop the service first (<c>sudo systemctl stop lidar-agv</c>): two programs reading
/// the same serial port would each get half of the data.</para>
/// </remarks>
public sealed class LidarCheck
{
    // Robot-frame directions checked (degrees, counter-clockwise from forward) and the +/- window.
    private static readonly (string Name, float Deg)[] Sectors = [("front", 0f), ("left", 90f), ("back", 180f), ("right", -90f)];
    private const float SectorHalfWidthDeg = 10f;

    private readonly LidarDevice _device;
    private readonly ConfigStore _config;
    private readonly AppOptions _options;

    /// <summary>Creates the check with the same LiDAR device and config the service would use.</summary>
    public LidarCheck(LidarDevice device, ConfigStore config, IOptions<AppOptions> options)
    {
        _device = device;
        _config = config;
        _options = options.Value;
    }

    /// <summary>Runs the check and prints the results to the console.</summary>
    /// <param name="scans">Number of revolutions to read.</param>
    /// <returns>Process exit code: 0 = OK, 1 = the LiDAR could not be read.</returns>
    public int Run(int scans)
    {
        Console.WriteLine($"LiDAR check on {_device.Description} ({scans} revolutions). Stop the lidar-agv service first if it is running.");
        try
        {
            _device.Open();
            LidarDeviceInfo info = _device.Info!;
            LidarHealth health = _device.Health!;
            Console.WriteLine($"model     {info.ModelName}{(info.IsA1Series ? "" : "  <-- NOT an A1: DTR motor control may not work")}");
            Console.WriteLine($"firmware  {info.Firmware}   hardware {info.Hardware}   serial {info.SerialNumber}");
            Console.WriteLine($"health    {health.Status} (code {health.ErrorCode})");
            Console.WriteLine();

            RuntimeConfig cfg = _config.Current;
            // Timed from the FIRST revolution, so motor spin-up does not distort the scan rate.
            var watch = new Stopwatch();
            int n = 0;
            using var cts = new CancellationTokenSource();
            foreach (IReadOnlyList<RawMeasurement> raw in _device.ScanRevolutions(cts.Token))
            {
                n++;
                if (n == 1) watch.Start();
                ProcessedScan scan = ScanProcessor.Process(raw, cfg, _options.Lidar);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"scan {n,3}: {raw.Count,4} raw {scan.Points.Count,4} valid   {SectorSummary(scan)}   objects {scan.Objects.Count}"));
                if (n >= scans) break;
            }
            double seconds = watch.Elapsed.TotalSeconds;
            Console.WriteLine();
            double rate = n > 1 && seconds > 0 ? (n - 1) / seconds : 0;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{n} scans, {rate:0.0} Hz between revolutions (A1M8 nominal 5.5-10 Hz)"));
            Console.WriteLine("Orientation: an object ~0.5 m to the LEFT must show under 'left'. See README troubleshooting if not.");
            return 0;
        }
        catch (LidarException ex)
        {
            Console.Error.WriteLine($"LiDAR check FAILED: {ex.Message}");
            return 1;
        }
        catch (Exception ex)   // anything unexpected: still report it cleanly and stop the motor
        {
            Console.Error.WriteLine($"LiDAR check FAILED with an unexpected error: {ex}");
            return 1;
        }
        finally
        {
            _device.Close();   // always stop the motor
        }
    }

    /// <summary>"front 1.23m  left 0.50m  back  --    right 2.10m" from the closest point within +/-10 deg of each direction.</summary>
    private static string SectorSummary(ProcessedScan scan)
    {
        var parts = new List<string>(Sectors.Length);
        foreach (var (name, deg) in Sectors)
        {
            float best = float.PositiveInfinity;
            foreach (ScanPoint p in scan.Points)
            {
                if (Math.Abs(ScanProcessor.NormalizeDeg(p.AngleDeg - deg)) <= SectorHalfWidthDeg && p.RangeM < best) best = p.RangeM;
            }
            parts.Add(float.IsPositiveInfinity(best)
                ? $"{name} --   "
                : string.Create(CultureInfo.InvariantCulture, $"{name} {best:0.00}m"));
        }
        return string.Join("  ", parts);
    }
}
