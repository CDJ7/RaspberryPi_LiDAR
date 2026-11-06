using System.Globalization;

namespace LidarPC.Api;

/// <summary>
/// Typed client for the LidarPi REST API: one method per endpoint, each returning the
/// deserialised response. All transport details live in <see cref="RestClientBase"/>.
/// </summary>
public sealed class LidarPiClient : RestClientBase
{
    /// <summary>Default REST port of the Pi program.</summary>
    public const int DefaultPort = 5000;

    /// <summary>Creates a client for <paramref name="baseAddress"/> with a 2.5 s timeout (fast enough to show a dead link quickly).</summary>
    public LidarPiClient(Uri baseAddress, string username, string password)
        : base(baseAddress, username, password, TimeSpan.FromSeconds(2.5)) { }

    /// <summary>
    /// Builds the base address from what the user typed: "192.168.1.100", "tikka.local",
    /// "tikka:5000" or a full "http://..." URL all work.
    /// </summary>
    /// <exception cref="FormatException">If the text cannot be a host name.</exception>
    public static Uri BuildBaseAddress(string hostText, int port)
    {
        string text = hostText.Trim();
        if (text.Length == 0) throw new FormatException("Enter the Pi's IP address or host name.");
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)) throw new FormatException($"'{hostText}' is not a valid host name or IP address.");
        // Use the port typed into the host box if there was one, otherwise the port field.
        // (IsDefaultPort is false only when a non-default port was written, e.g. "tikka:5000").
        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? port : uri.Port, "/");
        return builder.Uri;
    }

    /// <summary>GET /api/scan: points, objects and status for the live view.</summary>
    public Task<ScanResponse> GetScanAsync(CancellationToken ct = default) => GetAsync<ScanResponse>("api/scan", ct);

    /// <summary>GET /api/status: compact status.</summary>
    public Task<StatusResponse> GetStatusAsync(CancellationToken ct = default) => GetAsync<StatusResponse>("api/status", ct);

    /// <summary>POST /api/leds with mode "auto".</summary>
    public Task<LedState> SetLedsAutoAsync(CancellationToken ct = default) =>
        PostAsync<LedState>("api/leds", new { mode = "auto" }, ct);

    /// <summary>POST /api/leds with mode "test": sets the LEDs by hand for <paramref name="timeoutSeconds"/>.</summary>
    public Task<LedState> SetLedsTestAsync(bool green, bool yellow, bool red, int timeoutSeconds, CancellationToken ct = default) =>
        PostAsync<LedState>("api/leds", new { mode = "test", green, yellow, red, timeoutSeconds }, ct);

    /// <summary>GET /api/estop: reads the E-stop input.</summary>
    public Task<EStopState> GetEStopAsync(CancellationToken ct = default) => GetAsync<EStopState>("api/estop", ct);

    /// <summary>POST /api/estop/reset. Throws <see cref="ApiException"/> (Rejected) if the button is still pressed.</summary>
    public Task<ApiMessage> ResetEStopAsync(CancellationToken ct = default) => PostAsync<ApiMessage>("api/estop/reset", new { }, ct);

    /// <summary>POST /api/lcd: shows a custom message for <paramref name="seconds"/>.</summary>
    public Task<ApiMessage> SetLcdAsync(string line1, string line2, int seconds, CancellationToken ct = default) =>
        PostAsync<ApiMessage>("api/lcd", new { line1, line2, seconds }, ct);

    /// <summary>GET /api/config.</summary>
    public Task<ConfigResponse> GetConfigAsync(CancellationToken ct = default) => GetAsync<ConfigResponse>("api/config", ct);

    /// <summary>PUT /api/config: saves settings on the Pi (they survive a power cycle).</summary>
    public Task<ConfigResponse> UpdateConfigAsync(ConfigUpdate update, CancellationToken ct = default) =>
        PutAsync<ConfigResponse>("api/config", update, ct);

    /// <summary>PUT /api/config/password.</summary>
    public Task<ApiMessage> ChangePasswordAsync(string currentPassword, string newPassword, string? newUsername, CancellationToken ct = default) =>
        PutAsync<ApiMessage>("api/config/password", new { currentPassword, newPassword, newUsername }, ct);

    /// <summary>GET /api/system: OS, RAM, uptime, IP and LiDAR details.</summary>
    public Task<SystemResponse> GetSystemAsync(CancellationToken ct = default) => GetAsync<SystemResponse>("api/system", ct);

    /// <summary>POST /api/shutdown with confirm=true. The Pi answers first, then powers off.</summary>
    public Task<ApiMessage> ShutdownAsync(CancellationToken ct = default) => PostAsync<ApiMessage>("api/shutdown", new { confirm = true }, ct);

    /// <summary>POST /api/sim (only when the Pi app runs with --simulate): press the E-stop or unplug the LiDAR.</summary>
    public Task<Dictionary<string, bool>> SetSimulationAsync(bool? estopPressed, bool? lidarUnplugged, CancellationToken ct = default) =>
        PostAsync<Dictionary<string, bool>>("api/sim", new { estopPressed, lidarUnplugged }, ct);

    /// <summary>Formats an I2C address for display, e.g. 39 -> "0x27".</summary>
    public static string FormatI2cAddress(int address) => "0x" + address.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>Parses "0x27", "27h" or "39" into an address.</summary>
    /// <returns>True if the text is a valid 7-bit address.</returns>
    public static bool TryParseI2cAddress(string text, out int address)
    {
        string t = text.Trim();
        bool ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address)
            : t.EndsWith('h') || t.EndsWith('H')
                ? int.TryParse(t[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address)
                : int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out address);
        return ok && address is >= 0x03 and <= 0x77;
    }
}
