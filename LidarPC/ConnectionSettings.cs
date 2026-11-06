using System.Text.Json;

namespace LidarPC;

/// <summary>
/// Remembers the last Pi host, port and user name between runs of the GUI, in
/// %LOCALAPPDATA%\LidarPC\settings.json. The password is deliberately NOT saved.
/// </summary>
public sealed class ConnectionSettings
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LidarPC", "settings.json");

    /// <summary>Pi IP address or host name. Defaults to the lab Pi "tikka" at 192.168.1.100.</summary>
    public string Host { get; set; } = "192.168.1.100";

    /// <summary>REST API port.</summary>
    public int Port { get; set; } = 5000;

    /// <summary>REST API user name.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>Loads the saved settings, or defaults if there are none / the file is unreadable.</summary>
    public static ConnectionSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(FilePath)) ?? new ConnectionSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken settings file is not worth an error dialog: start with defaults.
        }
        return new ConnectionSettings();
    }

    /// <summary>Saves the settings; failures are ignored (convenience only).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not critical: the user just has to type the host again next time.
        }
    }
}
