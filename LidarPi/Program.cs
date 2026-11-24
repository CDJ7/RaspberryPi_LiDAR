using System.Text.Json.Serialization;
using LidarPi.Api;
using LidarPi.Config;
using LidarPi.Hardware;
using LidarPi.Lidar;
using LidarPi.Safety;
using LidarPi.Services;
using LidarPi.Tools;
using Microsoft.AspNetCore.Authentication;

namespace LidarPi;

/// <summary>
/// Entry point of the Pi program. Builds the ASP.NET Core host that runs, side by side:
/// the LiDAR reader thread, the 10 ms safety loop, the LCD thread and the Kestrel REST API.
/// </summary>
/// <remarks>
/// Command-line flags (handled here, then removed before the host sees the arguments):
/// <list type="bullet">
/// <item><c>--simulate</c>: fake moving LiDAR scene; GPIO and LCD actions are only logged.
/// Use it to run the whole system on the Windows PC.</item>
/// <item><c>--check [--scans N]</c>: LiDAR smoke test (info, health, scan rate,
/// front/left/back/right distances), then exit. Can be combined with <c>--simulate</c>.</item>
/// <item><c>--gpio-test [--seconds N]</c>: wiring check - each LED, the motion-enable pin,
/// then N seconds (default 20) of live E-stop readings, then exit.</item>
/// </list>
/// </remarks>
public static class Program
{
    /// <summary>Parses our flags, then runs either the service or one of the test tools.</summary>
    /// <returns>Process exit code (0 = OK).</returns>
    public static int Main(string[] args)
    {
        bool simulate = HasFlag(args, "--simulate");
        bool check = HasFlag(args, "--check");
        bool gpioTest = HasFlag(args, "--gpio-test");
        int scansIndex = IndexOfFlag(args, "--scans");
        int secondsIndex = IndexOfFlag(args, "--seconds");
        int scans = IntAfter(args, scansIndex, 20, 1, 1000);
        int seconds = IntAfter(args, secondsIndex, 20, 1, 600);

        // The generic host's command-line parser expects "--key value" pairs and would choke on
        // our bare switches, so strip them (and the numbers after --scans/--seconds) before
        // handing the rest (e.g. --urls) to the host.
        string[] hostArgs = args
            .Where((a, i) => !IsOurFlag(a) && i != scansIndex + 1 && i != secondsIndex + 1)
            .ToArray();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = hostArgs,
            // The systemd service starts us with WorkingDirectory set, but be safe: always read
            // appsettings.json from the folder that contains the executable.
            ContentRootPath = AppContext.BaseDirectory,
        });

        ConfigureServices(builder, simulate, toolMode: check || gpioTest);
        WebApplication app = builder.Build();

        // Tool modes: no web server and no background services; run the test, then exit.
        if (check) return app.Services.GetRequiredService<LidarCheck>().Run(scans);
        if (gpioTest) return app.Services.GetRequiredService<GpioCheck>().Run(seconds);

        AppOptions options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppOptions>>().Value;
        app.Logger.LogInformation("LidarAgv starting ({Mode}), REST API on http://{Addr}:{Port}",
            simulate ? "SIMULATION" : "hardware", options.ListenAddress, options.HttpPort);

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapLidarApi();
        app.Run();
        return 0;
    }

    /// <summary>True if the command line contains <paramref name="flag"/> (case-insensitive).</summary>
    private static bool HasFlag(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    /// <summary>Position of <paramref name="flag"/> in the arguments, or -2 if absent (so "index + 1" never matches a real argument).</summary>
    private static int IndexOfFlag(string[] args, string flag)
    {
        int i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? -2 : i;
    }

    /// <summary>The integer after a flag such as <c>--scans 20</c>, clamped to a sane range, or the default.</summary>
    private static int IntAfter(string[] args, int flagIndex, int fallback, int min, int max) =>
        flagIndex >= 0 && flagIndex + 1 < args.Length && int.TryParse(args[flagIndex + 1], out int n)
            ? Math.Clamp(n, min, max)
            : fallback;

    /// <summary>Our own switches, which are removed before the host parses the command line.</summary>
    private static bool IsOurFlag(string arg) =>
        arg.Equals("--simulate", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--check", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--gpio-test", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--scans", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--seconds", StringComparison.OrdinalIgnoreCase);

    /// <summary>Registers options, hardware wrappers, background services and the API.</summary>
    private static void ConfigureServices(WebApplicationBuilder builder, bool simulate, bool toolMode)
    {
        // Integrates with systemd: "Type=notify" readiness, journal log format, SIGTERM -> clean stop.
        // It is a no-op when not started by systemd (e.g. F5 in Visual Studio).
        builder.Host.UseSystemd();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss.fff ";
        });

        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
        builder.Services.PostConfigure<AppOptions>(o => o.Simulate = simulate);

        AppOptions bootOptions = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
        builder.WebHost.UseUrls($"http://{bootOptions.ListenAddress}:{bootOptions.HttpPort}");
        builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(8));

        // Shared singletons (one instance for the whole program).
        builder.Services.AddSingleton<ConfigStore>();
        builder.Services.AddSingleton<SimulationControl>();
        builder.Services.AddSingleton<GpioChipLocator>();
        builder.Services.AddSingleton<LedController>();
        builder.Services.AddSingleton<SystemInfoService>();
        builder.Services.AddSingleton<ShutdownService>();
        builder.Services.AddTransient<ApiDeps>();

        // The one place where simulation and hardware differ for the LiDAR: everything else
        // only sees the abstract LidarDevice.
        if (simulate) builder.Services.AddSingleton<LidarDevice, SimulatedLidar>();
        else builder.Services.AddSingleton<LidarDevice, RpLidarA1>();

        if (toolMode)
        {
            // The test tools print their own results; keep the log to warnings so the output stays readable.
            // (Set through configuration because appsettings.json's "Information" would otherwise win.)
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",
            });
            builder.Services.AddSingleton<LidarCheck>();
            builder.Services.AddSingleton<GpioCheck>();
            return;
        }

        // Background services. Each is registered once as a singleton AND as a hosted service,
        // so the API can talk to the same instance that the host starts and stops.
        // Stop order is the reverse of this list: LCD, then safety (motion LOW, LEDs off), then LiDAR (motor off).
        builder.Services.AddSingleton<LidarService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LidarService>());
        builder.Services.AddSingleton<SafetyMonitor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SafetyMonitor>());
        builder.Services.AddSingleton<LcdDisplayManager>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LcdDisplayManager>());

        // REST API: Basic auth on every endpoint.
        builder.Services.AddAuthentication(BasicAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BasicAuthHandler>(BasicAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    }
}
