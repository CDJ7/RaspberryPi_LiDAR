using System.Text.Json.Serialization;

namespace LidarPi.Config;

/// <summary>Which part of the scan counts toward the closest-obstacle / zone decision.</summary>
public enum SectorMode
{
    /// <summary>All 360 degrees (default: an AGV can be approached from any side).</summary>
    Full,
    /// <summary>Only +/- <see cref="RuntimeConfig.FrontHalfAngleDeg"/> around forward.</summary>
    Front,
}

/// <summary>
/// Operator-tunable settings that the REST API can change and that must survive a power
/// cycle. Saved as JSON by <see cref="ConfigStore"/>.
/// </summary>
/// <remarks>
/// An immutable record: a change produces a new instance (<c>with</c> expression) that is
/// swapped in atomically, so the safety loop never sees a half-applied update
/// (for example a new stop distance with the old caution distance).
/// </remarks>
public sealed record RuntimeConfig
{
    /// <summary>Closer than this = STOP / red (config.yaml safety.stop_distance_m).</summary>
    public double StopDistanceM { get; init; } = 0.35;

    /// <summary>Closer than this = CAUTION / yellow (config.yaml safety.slow_distance_m).</summary>
    public double CautionDistanceM { get; init; } = 0.80;

    /// <summary>Extra distance needed to move to a less severe zone, so the LEDs do not flicker at a boundary.</summary>
    public double HysteresisM { get; init; } = 0.05;

    /// <summary>Full 360 degrees or front-only monitoring.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SectorMode>))]
    public SectorMode SectorMode { get; init; } = SectorMode.Full;

    /// <summary>Half-angle of the front sector (config.yaml safety.lidar_front_half_angle_deg).</summary>
    public double FrontHalfAngleDeg { get; init; } = 35.0;

    /// <summary>Angle of the LiDAR's 0 degrees relative to robot-forward, counter-clockwise positive.</summary>
    public double AngleOffsetDeg { get; init; }

    /// <summary>Neighbouring points further apart than this start a new object.</summary>
    public double ClusterGapM { get; init; } = 0.15;

    /// <summary>Clusters with fewer points than this are dropped as noise.</summary>
    public int MinClusterPoints { get; init; } = 3;

    /// <summary>I2C address of the PCF8574 backpack (find it with <c>i2cdetect -y 1</c>; usually 0x27 or 0x3F).</summary>
    public int LcdI2cAddress { get; init; } = 0x27;

    /// <summary>REST API user name.</summary>
    public string Username { get; init; } = "admin";

    /// <summary>Base64 PBKDF2 hash of the password (never the password itself).</summary>
    public string PasswordHash { get; init; } = "";

    /// <summary>Base64 random salt used for <see cref="PasswordHash"/>.</summary>
    public string PasswordSalt { get; init; } = "";

    /// <summary>PBKDF2 iteration count stored with the hash so it can be raised later without breaking old files.</summary>
    public int PasswordIterations { get; init; } = PasswordHasher.DefaultIterations;

    /// <summary>
    /// Checks every value for sane limits. WHY here: both the API and the file loader use it,
    /// so a hand-edited config file cannot put the safety logic into a nonsense state either.
    /// </summary>
    /// <returns>An error message, or null if the configuration is valid.</returns>
    public string? Validate()
    {
        if (StopDistanceM is < 0.15 or > 5.0) return "stopDistanceM must be between 0.15 and 5.0 m (the A1 cannot see closer than 0.15 m).";
        if (CautionDistanceM is < 0.2 or > 8.0) return "cautionDistanceM must be between 0.2 and 8.0 m.";
        if (CautionDistanceM <= StopDistanceM) return "cautionDistanceM must be larger than stopDistanceM.";
        if (HysteresisM is < 0 or > 0.3) return "hysteresisM must be between 0 and 0.3 m.";
        if (FrontHalfAngleDeg is < 5 or > 180) return "frontHalfAngleDeg must be between 5 and 180 degrees.";
        if (AngleOffsetDeg is < -180 or > 360) return "angleOffsetDeg must be between -180 and 360 degrees.";
        if (ClusterGapM is < 0.02 or > 1.0) return "clusterGapM must be between 0.02 and 1.0 m.";
        if (MinClusterPoints is < 1 or > 50) return "minClusterPoints must be between 1 and 50.";
        if (LcdI2cAddress is < 0x03 or > 0x77) return "lcdI2cAddress must be a 7-bit I2C address (0x03-0x77).";
        if (string.IsNullOrWhiteSpace(Username) || Username.Contains(':')) return "username must not be empty or contain ':'.";
        return null;
    }
}
