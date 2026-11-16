using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LidarPi.Config;

/// <summary>
/// Owns the nonvolatile <see cref="RuntimeConfig"/>: loads it at boot, saves changes
/// atomically, and checks REST API credentials against the stored password hash.
/// </summary>
/// <remarks>
/// Atomic save = write a temp file in the same folder, flush it to the SD card, then
/// rename it over the old file. A rename is atomic on Linux, so pulling the power in the
/// middle of a save leaves either the complete old file or the complete new one, never a
/// half-written JSON file that would fail to load at the next boot.
/// </remarks>
public sealed class ConfigStore
{
    /// <summary>Password used until the operator changes it through the API.</summary>
    public const string DefaultPassword = "changeme";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // How long a successful login is remembered. WHY: the GUI polls ~10 times per second
    // and every request carries Basic credentials; re-running PBKDF2 (tens of ms) for each
    // one would waste a large part of a CPU core on the Pi.
    private static readonly TimeSpan LoginCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly ILogger<ConfigStore> _logger;
    private readonly object _saveLock = new();                                      // serialises writers (API threads)
    private readonly ConcurrentDictionary<string, DateTime> _loginCache = new();  // SHA-256(credentials) -> expiry
    private volatile RuntimeConfig _current;                                      // swapped atomically; readers never lock

    /// <summary>Full path of the JSON file (shown in logs and in GET /api/system).</summary>
    public string FilePath { get; }

    /// <summary>Raised after a change was saved, so components such as the LCD can re-apply settings.</summary>
    public event Action<RuntimeConfig>? Changed;

    /// <summary>Loads (or creates) the config file. Never throws: a broken file falls back to defaults.</summary>
    public ConfigStore(IOptions<AppOptions> options, ILogger<ConfigStore> logger)
    {
        _logger = logger;
        FilePath = ResolvePath(options.Value);
        _current = LoadOrCreate();
        IsDefaultPassword = CheckIsDefaultPassword(_current);
        if (IsDefaultPassword)
            _logger.LogWarning("REST API still uses the default password ({User} / {Password}). Change it from the GUI.", _current.Username, DefaultPassword);
    }

    /// <summary>The current configuration snapshot. Cheap to call from any thread.</summary>
    public RuntimeConfig Current => _current;

    /// <summary>True while the password is still the shipped default (shown as a warning in the API/GUI).</summary>
    /// <remarks>Cached because checking it costs a full PBKDF2 run; refreshed when the password changes.</remarks>
    public bool IsDefaultPassword { get; private set; }

    /// <summary>
    /// Applies a change, validates it and saves it to disk before making it current.
    /// </summary>
    /// <param name="change">Function that returns the modified copy (use a <c>with</c> expression).</param>
    /// <returns>Null on success, otherwise a validation or I/O error message (nothing is changed then).</returns>
    public string? Update(Func<RuntimeConfig, RuntimeConfig> change)
    {
        lock (_saveLock)
        {
            RuntimeConfig updated = change(_current);
            string? error = updated.Validate();
            if (error is not null) return error;
            try
            {
                SaveAtomically(updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Could not save config to {Path}", FilePath);
                return $"Could not save the configuration: {ex.Message}";
            }
            _current = updated;
        }
        _logger.LogInformation("Configuration saved to {Path}", FilePath);
        Changed?.Invoke(_current);
        return null;
    }

    /// <summary>Changes the login after checking the current password.</summary>
    /// <returns>Null on success, otherwise the reason it was refused.</returns>
    public string? ChangePassword(string currentPassword, string newPassword, string? newUsername)
    {
        RuntimeConfig cfg = _current;
        if (!PasswordHasher.Verify(currentPassword, cfg.PasswordSalt, cfg.PasswordHash, cfg.PasswordIterations))
            return "The current password is wrong.";
        if (newPassword.Length < 6) return "The new password must be at least 6 characters.";
        var (salt, hash, iterations) = PasswordHasher.Hash(newPassword);
        string? error = Update(c => c with
        {
            Username = string.IsNullOrWhiteSpace(newUsername) ? c.Username : newUsername.Trim(),
            PasswordSalt = salt,
            PasswordHash = hash,
            PasswordIterations = iterations,
        });
        if (error is null)
        {
            _loginCache.Clear();   // old credentials must stop working immediately
            IsDefaultPassword = CheckIsDefaultPassword(_current);
        }
        return error;
    }

    /// <summary>Checks a Basic-auth user name and password (used by <see cref="Api.BasicAuthHandler"/>).</summary>
    /// <returns>True if they match the stored login.</returns>
    public bool VerifyCredentials(string username, string password)
    {
        RuntimeConfig cfg = _current;
        // Cache key includes the stored hash, so a password change invalidates old entries automatically.
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{username}\0{password}\0{cfg.PasswordHash}")));
        if (_loginCache.TryGetValue(key, out DateTime expiry) && expiry > DateTime.UtcNow) return true;

        bool ok = string.Equals(username, cfg.Username, StringComparison.Ordinal)
                  && PasswordHasher.Verify(password, cfg.PasswordSalt, cfg.PasswordHash, cfg.PasswordIterations);
        if (ok)
        {
            if (_loginCache.Count > 32) _loginCache.Clear();   // bound memory if many different logins are tried
            _loginCache[key] = DateTime.UtcNow + LoginCacheLifetime;
        }
        return ok;
    }

    /// <summary>Runs PBKDF2 once to see whether the stored hash is the default password's.</summary>
    private static bool CheckIsDefaultPassword(RuntimeConfig cfg) =>
        PasswordHasher.Verify(DefaultPassword, cfg.PasswordSalt, cfg.PasswordHash, cfg.PasswordIterations);

    /// <summary>Picks the config file location (see <see cref="AppOptions.ConfigPath"/>).</summary>
    private static string ResolvePath(AppOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigPath)) return options.ConfigPath;
        // In simulation (typically on the Windows PC) /var/lib does not exist; keep the file next to the exe.
        if (options.Simulate || !OperatingSystem.IsLinux())
            return Path.Combine(AppContext.BaseDirectory, "data", "config.json");
        return "/var/lib/lidar-agv/config.json";
    }

    /// <summary>Reads the file; creates it with defaults (and the default password) on first boot.</summary>
    private RuntimeConfig LoadOrCreate()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                RuntimeConfig? loaded = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(FilePath), JsonOptions);
                string? error = loaded?.Validate();
                if (loaded is not null && error is null && loaded.PasswordHash.Length > 0)
                {
                    _logger.LogInformation("Loaded configuration from {Path}", FilePath);
                    return loaded;
                }
                // Keep the bad file for inspection instead of silently overwriting it.
                string badPath = FilePath + ".bad";
                File.Copy(FilePath, badPath, overwrite: true);
                _logger.LogError("Config file {Path} is invalid ({Error}); using defaults. Old file kept as {Bad}",
                    FilePath, error ?? "missing password hash", badPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(ex, "Could not read config file {Path}; using defaults", FilePath);
        }

        var (salt, hash, iterations) = PasswordHasher.Hash(DefaultPassword);
        var defaults = new RuntimeConfig { PasswordSalt = salt, PasswordHash = hash, PasswordIterations = iterations };
        try
        {
            SaveAtomically(defaults);
            _logger.LogWarning("Created default configuration at {Path}", FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep running with in-memory defaults; the API reports the save error when someone tries to change settings.
            _logger.LogError(ex, "Could not create config file {Path}; running with defaults in memory only", FilePath);
        }
        return defaults;
    }

    /// <summary>Temp file + fsync + rename, so the file on disk is always complete (see class remarks).</summary>
    private void SaveAtomically(RuntimeConfig cfg)
    {
        string dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        string tmp = FilePath + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, cfg, JsonOptions);
            stream.Flush(flushToDisk: true);   // make sure the bytes are on the SD card before the rename
        }
        File.Move(tmp, FilePath, overwrite: true);
    }
}
