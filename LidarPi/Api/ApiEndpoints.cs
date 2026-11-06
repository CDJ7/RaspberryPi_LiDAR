using LidarPi.Config;
using LidarPi.Hardware;
using LidarPi.Lidar;
using LidarPi.Safety;
using LidarPi.Services;
using Microsoft.Extensions.Options;

namespace LidarPi.Api;

/// <summary>
/// Maps every REST endpoint (ASP.NET Core minimal API). All routes are under <c>/api</c>
/// and require Basic authentication.
/// </summary>
/// <remarks>
/// The endpoints are thin: they validate input, call the component that owns the job
/// (LED controller, E-stop latch, LCD manager, config store...) and translate the result to
/// JSON. No safety logic lives here, so a bad request can never bypass the safety loop.
/// </remarks>
public static class ApiEndpoints
{
    /// <summary>Registers all routes on the application.</summary>
    public static void MapLidarApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api").RequireAuthorization();

        // ---- live data ------------------------------------------------------------------
        api.MapGet("/scan", GetScan);
        api.MapGet("/status", (ApiDeps d) => Results.Ok(BuildStatus(d)));

        // ---- output: LEDs (course requirement "REST command that changes an output") ----
        api.MapGet("/leds", (ApiDeps d) => Results.Ok(BuildLeds(d)));
        api.MapPost("/leds", SetLeds);

        // ---- input: E-stop (course requirement "observes an input and returns its state")
        api.MapGet("/estop", GetEStop);
        api.MapPost("/estop/reset", (ApiDeps d, ILoggerFactory lf) =>
        {
            var (ok, message) = d.Safety.EStop.TryReset();
            lf.CreateLogger("Api").LogInformation("E-stop reset requested: {Result}", message);
            return ok ? Results.Ok(new ApiMessage(message)) : Results.Conflict(new ApiError(message));
        });

        // ---- LCD -----------------------------------------------------------------------
        api.MapGet("/lcd", (ApiDeps d) => Results.Ok(d.Lcd.GetState()));
        api.MapPost("/lcd", (LcdRequest req, ApiDeps d) =>
        {
            if (req.Seconds is < 1 or > 3600) return Results.BadRequest(new ApiError("seconds must be 1-3600."));
            d.Lcd.ShowCustomMessage(req.Line1 ?? "", req.Line2 ?? "", req.Seconds);
            return Results.Ok(new ApiMessage($"Message shown for {req.Seconds} s (E-stop/fault/STOP warnings take priority)."));
        });

        // ---- nonvolatile configuration ---------------------------------------------------
        api.MapGet("/config", (ApiDeps d) => Results.Ok(BuildConfig(d.Config)));
        api.MapPut("/config", UpdateConfig);
        api.MapPut("/config/password", (PasswordChangeRequest req, ApiDeps d) =>
        {
            string? error = d.Config.ChangePassword(req.CurrentPassword ?? "", req.NewPassword ?? "", req.NewUsername);
            return error is null
                ? Results.Ok(new ApiMessage("Password changed and saved. Log in again with the new password."))
                : Results.BadRequest(new ApiError(error));
        });

        // ---- system information + shutdown ----------------------------------------------
        api.MapGet("/system", GetSystem);
        api.MapPost("/shutdown", Shutdown);

        // ---- simulation helpers (only exist with --simulate) -----------------------------
        if (app.Services.GetRequiredService<IOptions<AppOptions>>().Value.Simulate)
        {
            api.MapPost("/sim", (SimRequest req, SimulationControl sim, ILoggerFactory lf) =>
            {
                ILogger log = lf.CreateLogger("Simulation");
                if (req.EStopPressed is bool p) { sim.EStopPressed = p; log.LogInformation("[SIM] E-stop button {State}", p ? "PRESSED" : "released"); }
                if (req.LidarUnplugged is bool u) { sim.LidarUnplugged = u; log.LogInformation("[SIM] LiDAR {State}", u ? "UNPLUGGED" : "plugged in"); }
                return Results.Ok(new { estopPressed = sim.EStopPressed, lidarUnplugged = sim.LidarUnplugged });
            });
        }
    }

    /// <summary>GET /api/scan: downsampled points + objects + status.</summary>
    private static IResult GetScan(ApiDeps d)
    {
        RuntimeConfig cfg = d.Config.Current;
        ProcessedScan? scan = d.Lidar.Scans.Get();
        return Results.Ok(new ScanResponse(
            BuildStatus(d),
            scan?.TimestampUtc,
            scan is null ? [] : Downsample(scan.Points),
            scan?.Objects ?? [],
            cfg.StopDistanceM, cfg.CautionDistanceM, cfg.SectorMode.ToString(), cfg.FrontHalfAngleDeg,
            d.Lidar.Status.ScanRateHz));
    }

    /// <summary>
    /// Keeps the closest point in each 1-degree bin. WHY: a revolution can have ~1000+ points;
    /// 360 is plenty for the plot and keeps the 10 Hz poll small (~10 KB of JSON).
    /// The safety logic always uses the full-resolution scan, never this.
    /// </summary>
    private static List<float[]> Downsample(IReadOnlyList<ScanPoint> points)
    {
        var bins = new ScanPoint?[360];
        foreach (ScanPoint p in points)
        {
            int bin = ((int)MathF.Floor(p.AngleDeg) + 360) % 360;
            if (bins[bin] is not ScanPoint existing || p.RangeM < existing.RangeM) bins[bin] = p;
        }
        var result = new List<float[]>(360);
        foreach (ScanPoint? b in bins)
        {
            if (b is ScanPoint p) result.Add([MathF.Round(p.X, 3), MathF.Round(p.Y, 3)]);
        }
        return result;
    }

    /// <summary>Builds the compact status from the latest safety-loop result.</summary>
    private static StatusResponse BuildStatus(ApiDeps d)
    {
        SafetyStatus? s = d.Safety.Status.Get();
        LcdState lcd = d.Lcd.GetState();
        var faults = new List<string>(s?.HardwareFaults ?? []);
        if (lcd.Fault is not null && !d.Simulate) faults.Add(lcd.Fault);
        return new StatusResponse(
            s?.TimestampUtc ?? DateTime.UtcNow,
            s?.Zone ?? SafetyZone.Stop,
            s?.ClosestM is double c ? Math.Round(c, 3) : null,
            s?.BearingDeg is double b ? Math.Round(b, 1) : null,
            s?.MotionEnabled ?? false,
            s?.EStopLatched ?? true,
            s?.EStopPressed ?? false,
            s?.EStopReason,
            s?.LidarFault ?? true,
            s?.FaultMessage ?? (s is null ? "Safety loop not running yet" : null),
            faults,
            BuildLeds(d),
            lcd.Line1.Replace((char)0xDF, '°'),
            lcd.Line2,
            d.Shutdown.InProgress,
            d.Shutdown.Error,
            d.Simulate,
            d.Config.IsDefaultPassword);
    }

    /// <summary>Current LED mode and states.</summary>
    private static LedState BuildLeds(ApiDeps d)
    {
        LedController leds = d.Safety.Leds;
        var (g, y, r) = leds.States;
        return new LedState(leds.Mode, g, y, r, leds.TestSecondsRemaining);
    }

    /// <summary>POST /api/leds: auto mode, or a manual test pattern with a timeout.</summary>
    private static IResult SetLeds(LedRequest req, ApiDeps d)
    {
        switch (req.Mode?.Trim().ToLowerInvariant())
        {
            case "auto":
                d.Safety.Leds.SetAuto();
                break;
            case "test":
                if (req.TimeoutSeconds < 1) return Results.BadRequest(new ApiError("timeoutSeconds must be at least 1."));
                d.Safety.Leds.SetTest(req.Green, req.Yellow, req.Red, req.TimeoutSeconds);
                break;
            default:
                return Results.BadRequest(new ApiError("mode must be \"auto\" or \"test\"."));
        }
        return Results.Ok(BuildLeds(d));
    }

    /// <summary>GET /api/estop: the button's raw and debounced state plus the latch.</summary>
    private static IResult GetEStop(ApiDeps d)
    {
        EmergencyStopLatch e = d.Safety.EStop;
        SafetyStatus? s = d.Safety.Status.Get();
        string? inputFault = s?.HardwareFaults.FirstOrDefault(f => f.StartsWith("E-stop input", StringComparison.Ordinal));
        return Results.Ok(new EStopState(e.Pressed, e.RawPressed, e.RawPressed ? "HIGH" : "LOW", e.Latched, e.LatchReason,
            e.LastTripUtc, e.TripCount, inputFault));
    }

    /// <summary>Converts the stored config to its public JSON form (no password hash).</summary>
    private static ConfigResponse BuildConfig(ConfigStore store)
    {
        RuntimeConfig c = store.Current;
        return new ConfigResponse(c.StopDistanceM, c.CautionDistanceM, c.HysteresisM, c.SectorMode.ToString(), c.FrontHalfAngleDeg,
            c.AngleOffsetDeg, c.ClusterGapM, c.MinClusterPoints, c.LcdI2cAddress, c.Username, store.IsDefaultPassword, store.FilePath);
    }

    /// <summary>PUT /api/config: validates and saves (atomically) only the fields that were sent.</summary>
    private static IResult UpdateConfig(ConfigUpdateRequest req, ApiDeps d)
    {
        SectorMode? sector = null;
        if (req.SectorMode is not null)
        {
            if (!Enum.TryParse(req.SectorMode, ignoreCase: true, out SectorMode parsed))
                return Results.BadRequest(new ApiError("sectorMode must be \"Full\" or \"Front\"."));
            sector = parsed;
        }
        string? error = d.Config.Update(c => c with
        {
            StopDistanceM = req.StopDistanceM ?? c.StopDistanceM,
            CautionDistanceM = req.CautionDistanceM ?? c.CautionDistanceM,
            HysteresisM = req.HysteresisM ?? c.HysteresisM,
            SectorMode = sector ?? c.SectorMode,
            FrontHalfAngleDeg = req.FrontHalfAngleDeg ?? c.FrontHalfAngleDeg,
            AngleOffsetDeg = req.AngleOffsetDeg ?? c.AngleOffsetDeg,
            ClusterGapM = req.ClusterGapM ?? c.ClusterGapM,
            MinClusterPoints = req.MinClusterPoints ?? c.MinClusterPoints,
            LcdI2cAddress = req.LcdI2cAddress ?? c.LcdI2cAddress,
        });
        return error is null ? Results.Ok(BuildConfig(d.Config)) : Results.BadRequest(new ApiError(error));
    }

    /// <summary>GET /api/system: OS, RAM, uptime, IPs, plus LiDAR model/firmware/health/rate.</summary>
    private static IResult GetSystem(ApiDeps d, SystemInfoService info, GpioChipLocator gpio)
    {
        LidarStatus l = d.Lidar.Status;
        var lidar = new LidarSystemInfo(l.Device, l.Connected, l.Info?.ModelName, l.Info?.Firmware, l.Info?.Hardware,
            l.Info?.SerialNumber, l.Health?.Status, l.Health?.ErrorCode, l.ScanRateHz, l.ErrorCount, l.LastError);
        return Results.Ok(new SystemResponse(info.Read(), lidar, d.Lcd.GetState() with { Line1 = d.Lcd.GetState().Line1.Replace((char)0xDF, '°') },
            d.Simulate ? "simulated (no GPIO access)" : gpio.Description, d.Config.FilePath, d.Simulate));
    }

    /// <summary>POST /api/shutdown: answers 202 first, then runs the shutdown sequence.</summary>
    private static IResult Shutdown(ShutdownRequest req, ApiDeps d, HttpContext http)
    {
        if (!req.Confirm) return Results.BadRequest(new ApiError("Send {\"confirm\": true} to shut down."));
        if (d.Shutdown.InProgress) return Results.Conflict(new ApiError("Shutdown already in progress."));
        // OnCompleted runs after the response has been written to the client.
        http.Response.OnCompleted(() =>
        {
            d.Shutdown.TryBegin();
            return Task.CompletedTask;
        });
        return Results.Accepted(value: new ApiMessage(d.Simulate
            ? "Simulated shutdown: LEDs off, LCD message, LiDAR stopped, then the simulator exits."
            : "Shutting down: LEDs off, LCD message, LiDAR stopped, then the Pi powers off. Wait ~20 s before removing power."));
    }
}

/// <summary>
/// The services most endpoints need, bundled into one handler parameter so the endpoint
/// lambdas stay short. Registered in DI (Program.cs) so minimal APIs inject it instead of
/// trying to read it from the request body.
/// </summary>
internal sealed class ApiDeps(SafetyMonitor safety, LidarService lidar, LcdDisplayManager lcd, ConfigStore config,
    ShutdownService shutdown, IOptions<AppOptions> options)
{
    /// <summary>Safety loop (status, E-stop latch, LEDs).</summary>
    public SafetyMonitor Safety { get; } = safety;

    /// <summary>LiDAR reader (scans, status).</summary>
    public LidarService Lidar { get; } = lidar;

    /// <summary>LCD manager.</summary>
    public LcdDisplayManager Lcd { get; } = lcd;

    /// <summary>Nonvolatile config.</summary>
    public ConfigStore Config { get; } = config;

    /// <summary>Shutdown sequence.</summary>
    public ShutdownService Shutdown { get; } = shutdown;

    /// <summary>True with --simulate.</summary>
    public bool Simulate { get; } = options.Value.Simulate;
}
