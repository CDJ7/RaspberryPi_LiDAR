using System.Text.Json.Serialization;
using LidarPi.Api;
using LidarPi.Config;
using LidarPi.Hardware;
using LidarPi.Lidar;
using LidarPi.Safety;
using LidarPi.Services;
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
/// </list>
/// </remarks>
public static class Program
{
    /// <summary>Parses our flags, then runs either the service or the <c>--check</c> tool.</summary>
    /// <returns>Process exit code (0 = OK).</returns>
    public static int Main(string[] args)
    {
        bool simulate = args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);
        // The generic host's command-line parser expects "--key value" pairs and would choke on
        // our bare switches, so strip them before handing the rest (e.g. --urls) to the host.
        string[] hostArgs = args.Where(a => !a.Equals("--simulate", StringComparison.OrdinalIgnoreCase)).ToArray();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = hostArgs,
            // The systemd service starts us with WorkingDirectory set, but be safe: always read
            // appsettings.json from the folder that contains the executable.
            ContentRootPath = AppContext.BaseDirectory,
        });

        ConfigureServices(builder, simulate);
        WebApplication app = builder.Build();

        AppOptions options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppOptions>>().Value;
        app.Logger.LogInformation("LidarAgv starting ({Mode}), REST API on http://{Addr}:{Port}",
            simulate ? "SIMULATION" : "hardware", options.ListenAddress, options.HttpPort);

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapLidarApi();
        app.Run();
        return 0;
    }

    /// <summary>Registers options, hardware wrappers, background services and the API.</summary>
    private static void ConfigureServices(WebApplicationBuilder builder, bool simulate)
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

        if (simulate)
        {
            builder.Services.AddSingleton<LidarDevice, SimulatedLidar>();
        }
        else
        {
            // TODO (stage 3): the real RPLIDAR A1 driver (RpLidarA1) is registered here.
            throw new NotSupportedException("Stage 1 build: only --simulate is available until the RPLIDAR driver is added.");
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
