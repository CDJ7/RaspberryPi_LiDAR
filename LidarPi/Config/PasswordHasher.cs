using System.Security.Cryptography;
using System.Text;

namespace LidarPi.Config;

/// <summary>
/// Salted PBKDF2-SHA256 password hashing for the REST API login.
/// </summary>
/// <remarks>
/// WHY: the config file sits on the SD card. Storing a salted, slow hash means someone who
/// copies the file cannot read the password, and identical passwords on two Pis produce
/// different hashes. Uses only the .NET base library (System.Security.Cryptography).
/// </remarks>
public static class PasswordHasher
{
    /// <summary>
    /// PBKDF2 iterations. 100 000 takes roughly 30-60 ms on a Pi 5, which is slow for an attacker
    /// but fine for us because successful logins are cached (see <see cref="ConfigStore"/>).
    /// </summary>
    public const int DefaultIterations = 100_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Hashes a new password with a fresh random salt.</summary>
    /// <returns>Base64 salt, Base64 hash and the iteration count used.</returns>
    public static (string Salt, string Hash, int Iterations) Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, DefaultIterations, HashAlgorithmName.SHA256, HashBytes);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash), DefaultIterations);
    }

    /// <summary>Checks a password against a stored salt/hash pair.</summary>
    /// <returns>True if the password matches.</returns>
    public static bool Verify(string password, string saltB64, string hashB64, int iterations)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(saltB64);
            byte[] expected = Convert.FromBase64String(hashB64);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            // Constant-time compare so response timing does not leak how many bytes matched.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;   // corrupt hash in the file -> nobody can log in until it is fixed/reset
        }
    }
}
