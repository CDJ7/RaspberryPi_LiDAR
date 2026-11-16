using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LidarPi.Services;

/// <summary>System information returned by GET /api/system (together with LiDAR/LCD details).</summary>
/// <param name="Hostname">Machine name.</param>
/// <param name="OsVersion">PRETTY_NAME from /etc/os-release (e.g. "Debian GNU/Linux 12 (bookworm)").</param>
/// <param name="Kernel">Kernel / OS description from the runtime.</param>
/// <param name="MemAvailableMb">Available RAM (MemAvailable from /proc/meminfo) in MB.</param>
/// <param name="MemTotalMb">Total RAM in MB.</param>
/// <param name="SystemUptimeSeconds">Seconds since boot (/proc/uptime).</param>
/// <param name="ServiceUptimeSeconds">Seconds since this program started.</param>
/// <param name="IpAddresses">IPv4 addresses of all interfaces that are up (excluding loopback).</param>
/// <param name="CpuTemperatureC">SoC temperature, if readable.</param>
/// <param name="DotnetVersion">.NET runtime version.</param>
public sealed record SystemInfo(
    string Hostname, string OsVersion, string Kernel, double MemAvailableMb, double MemTotalMb,
    double SystemUptimeSeconds, double ServiceUptimeSeconds, IReadOnlyList<string> IpAddresses,
    double? CpuTemperatureC, string DotnetVersion);

/// <summary>
/// Reads OS version, memory, uptime and IP addresses for the "system information" requirement.
/// </summary>
/// <remarks>
/// On the Pi everything comes from the standard Linux files (/etc/os-release,
/// /proc/meminfo, /proc/uptime). WHY the fallbacks: with <c>--simulate</c> on the Windows PC
/// those files do not exist, and the endpoint should still return sensible values instead
/// of failing, so the GUI panel can be tested.
/// </remarks>
public sealed class SystemInfoService
{
    // Process start time, not "when this object was created": the service is created lazily on the first request.
    private readonly DateTime _startedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>Collects the current values (cheap: a few small file reads).</summary>
    public SystemInfo Read()
    {
        var (availMb, totalMb) = ReadMemory();
        return new SystemInfo(
            Environment.MachineName,
            ReadOsVersion(),
            ReadKernel(),
            availMb,
            totalMb,
            ReadUptimeSeconds(),
            Math.Round((DateTime.UtcNow - _startedUtc).TotalSeconds),
            ReadIpAddresses(),
            ReadCpuTemperature(),
            RuntimeInformation.FrameworkDescription);
    }

    /// <summary>PRETTY_NAME from /etc/os-release, or the runtime's OS description elsewhere.</summary>
    private static string ReadOsVersion()
    {
        try
        {
            foreach (string line in File.ReadLines("/etc/os-release"))
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    return line["PRETTY_NAME=".Length..].Trim('"');
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* not Linux: fall through */ }
        return RuntimeInformation.OSDescription;
    }

    /// <summary>Kernel release (/proc/sys/kernel/osrelease, e.g. "6.6.51+rpt-rpi-2712"), or the OS description elsewhere.</summary>
    private static string ReadKernel()
    {
        try { return "Linux " + File.ReadAllText("/proc/sys/kernel/osrelease").Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return RuntimeInformation.OSDescription; }
    }

    /// <summary>MemAvailable and MemTotal (kB in /proc/meminfo) converted to MB.</summary>
    private static (double AvailMb, double TotalMb) ReadMemory()
    {
        try
        {
            double avail = 0, total = 0;
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                // Format: "MemAvailable:    6123456 kB"
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0] == "MemAvailable:") avail = double.Parse(parts[1], CultureInfo.InvariantCulture) / 1024.0;
                else if (parts[0] == "MemTotal:") total = double.Parse(parts[1], CultureInfo.InvariantCulture) / 1024.0;
            }
            return (Math.Round(avail, 1), Math.Round(total, 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Windows (simulation): the GC knows the machine's physical memory; "available" is approximate.
            GCMemoryInfo gc = GC.GetGCMemoryInfo();
            double total = gc.TotalAvailableMemoryBytes / 1048576.0;
            return (Math.Round(total - gc.MemoryLoadBytes / 1048576.0, 1), Math.Round(total, 1));
        }
    }

    /// <summary>First number of /proc/uptime (seconds since boot), or the tick counter elsewhere.</summary>
    private static double ReadUptimeSeconds()
    {
        try
        {
            string first = File.ReadAllText("/proc/uptime").Split(' ')[0];
            return Math.Round(double.Parse(first, CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return Math.Round(Environment.TickCount64 / 1000.0);
        }
    }

    /// <summary>IPv4 addresses of interfaces that are up, e.g. "wlan0 192.168.1.42".</summary>
    private static List<string> ReadIpAddresses()
    {
        var result = new List<string>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    result.Add($"{nic.Name} {addr.Address}");
            }
        }
        return result;
    }

    /// <summary>SoC temperature in degrees C from the thermal zone (Pi only), else null.</summary>
    private static double? ReadCpuTemperature()
    {
        try
        {
            string milli = File.ReadAllText("/sys/class/thermal/thermal_zone0/temp").Trim();
            return Math.Round(double.Parse(milli, CultureInfo.InvariantCulture) / 1000.0, 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}
