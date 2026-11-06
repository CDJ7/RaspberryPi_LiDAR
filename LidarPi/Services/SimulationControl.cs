namespace LidarPi.Services;

/// <summary>
/// Switches that stand in for the physical world while running with <c>--simulate</c>:
/// pressing the E-stop button and unplugging the LiDAR. Changed through
/// <c>POST /api/sim</c> (only mapped in simulation) so every fail-safe path can be tested
/// from the GUI on the PC before anything is wired.
/// </summary>
public sealed class SimulationControl
{
    // volatile: written by API threads, read by the safety and LiDAR threads without a lock.
    private volatile bool _estopPressed;
    private volatile bool _lidarUnplugged;

    /// <summary>Simulated state of the mushroom button (true = pressed / wire broken).</summary>
    public bool EStopPressed { get => _estopPressed; set => _estopPressed = value; }

    /// <summary>Simulated USB cable state (true = unplugged, the LiDAR thread fails and reconnects).</summary>
    public bool LidarUnplugged { get => _lidarUnplugged; set => _lidarUnplugged = value; }
}
