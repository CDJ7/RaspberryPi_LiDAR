using LidarPi.Config;
using LidarPi.Services;
using Microsoft.Extensions.Options;

namespace LidarPi.Lidar;

/// <summary>
/// Fake RPLIDAR for <c>--simulate</c>: ray-casts a small room with a static box and two
/// moving "people", and reports the results with the same CLOCKWISE angle convention,
/// quality range and dropouts as the real A1M8 (port of <c>agv_pi/sim/sim_lidar.py</c>).
/// </summary>
/// <remarks>
/// WHY reproduce the raw convention instead of producing robot-frame points directly: the
/// real <see cref="ScanProcessor"/> (clockwise flip, offset, filters) is then exercised in
/// simulation too, so a sign mistake shows up on the PC before the hardware is wired.
/// One moving target walks straight toward the front of the robot and back over 20 s,
/// so the zone cycles CLEAR, CAUTION, STOP, CAUTION, CLEAR.
/// </remarks>
public sealed class SimulatedLidar : LidarDevice
{
    // Room walls around the robot (metres, robot frame: x forward, y left).
    private const double RoomXMin = -2.5, RoomXMax = 3.5, RoomYMin = -2.0, RoomYMax = 2.2;

    private readonly SimulationOptions _sim;
    private readonly SimulationControl _control;   // lets the API "unplug" the simulated LiDAR
    private readonly Random _random = new(7);      // fixed seed: repeatable runs

    /// <summary>Creates the simulator; options come from appsettings.json (LidarAgv:Simulation).</summary>
    public SimulatedLidar(IOptions<AppOptions> options, SimulationControl control, ILogger<SimulatedLidar> logger)
        : base(logger)
    {
        _sim = options.Value.Simulation;
        _control = control;
    }

    /// <inheritdoc/>
    public override string Description => "simulator";

    /// <inheritdoc/>
    protected override void ConnectCore()
    {
        if (_control.LidarUnplugged) throw new LidarException("simulated LiDAR is unplugged");
    }

    /// <inheritdoc/>
    protected override void DisconnectCore() { }

    /// <inheritdoc/>
    protected override LidarDeviceInfo ReadInfoCore() => new(0x18, "1.29", 7, "SIMULATED0000000000000000000000");

    /// <inheritdoc/>
    protected override LidarHealth ReadHealthCore() => new("Good", 0);

    /// <inheritdoc/>
    protected override void StopScanAndMotorCore() => Logger.LogInformation("[SIM] LiDAR motor stopped");

    /// <inheritdoc/>
    protected override IEnumerable<IReadOnlyList<RawMeasurement>> ScanCore(CancellationToken cancellationToken)
    {
        Logger.LogInformation("[SIM] LiDAR motor started, {Rate} Hz, {Beams} beams", _sim.RateHz, _sim.Beams);
        TimeSpan period = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, _sim.RateHz));
        DateTime start = DateTime.UtcNow;
        double angleJitter = _random.NextDouble();   // the real sensor's beams never start exactly at 0 deg

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_control.LidarUnplugged) throw new LidarException("simulated LiDAR was unplugged (scan data timeout)");
            DateTime t0 = DateTime.UtcNow;
            double t = (t0 - start).TotalSeconds;
            var scene = BuildScene(t);
            var revolution = new List<RawMeasurement>(_sim.Beams);

            for (int i = 0; i < _sim.Beams; i++)
            {
                double rawDeg = (i * 360.0 / _sim.Beams + angleJitter) % 360.0;
                double robotRad = -rawDeg * Math.PI / 180.0;   // raw is clockwise -> robot frame is CCW
                double d = Raycast(Math.Cos(robotRad), Math.Sin(robotRad), scene);
                if (double.IsInfinity(d) || _random.NextDouble() < 0.03) continue;   // 3 % dropouts like the real unit
                double noisy = d + Gaussian() * 0.01;                                   // ~1 cm range noise
                revolution.Add(new RawMeasurement((byte)_random.Next(15, 48), (float)rawDeg, (float)(noisy * 1000.0)));
            }

            yield return revolution;

            TimeSpan wait = period - (DateTime.UtcNow - t0);
            if (wait > TimeSpan.Zero) cancellationToken.WaitHandle.WaitOne(wait);
        }
    }

    /// <summary>Circles that make up the scene at time <paramref name="t"/>: (centre x, centre y, radius).</summary>
    private static (double X, double Y, double R)[] BuildScene(double t) =>
    [
        (1.6 + 1.4 * Math.Cos(t * 2 * Math.PI / 20.0), 0.0, 0.15),        // person walking toward the front and back
        (-0.4 + 1.2 * Math.Sin(t * 2 * Math.PI / 13.0), 1.1, 0.12),       // person crossing on the left side
        (-1.3, -1.0, 0.25),                                               // static box behind-right
        (2.4, -1.4, 0.10),                                                // static table leg front-right
    ];

    /// <summary>Distance along a ray from the origin to the nearest circle or room wall.</summary>
    private static double Raycast(double dx, double dy, (double X, double Y, double R)[] circles)
    {
        double best = double.PositiveInfinity;
        // Room walls (axis-aligned rectangle that contains the origin).
        if (dx > 1e-9) best = Math.Min(best, RoomXMax / dx);
        if (dx < -1e-9) best = Math.Min(best, RoomXMin / dx);
        if (dy > 1e-9) best = Math.Min(best, RoomYMax / dy);
        if (dy < -1e-9) best = Math.Min(best, RoomYMin / dy);
        // Circles: solve |t*d - c|^2 = r^2 for the smallest positive t.
        foreach (var (cx, cy, r) in circles)
        {
            double b = dx * cx + dy * cy;
            double disc = b * b - (cx * cx + cy * cy - r * r);
            if (disc < 0) continue;
            double hit = b - Math.Sqrt(disc);
            if (hit > 0) best = Math.Min(best, hit);
        }
        return best;
    }

    /// <summary>Standard normal random number (Box-Muller) for range noise.</summary>
    private double Gaussian() =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - _random.NextDouble())) * Math.Cos(2.0 * Math.PI * _random.NextDouble());
}
