namespace LidarPi.Config;

/// <summary>
/// Static settings read from <c>appsettings.json</c> (section <c>"LidarAgv"</c>) at startup.
/// </summary>
/// <remarks>
/// WHY two kinds of configuration: wiring-level facts (pins, serial port, timing of the
/// safety loop) belong to the installation and should not be editable over the network.
/// Operator tunables (distances, offset, password) live in <see cref="RuntimeConfig"/>,
/// which the REST API can change and which is saved to a separate JSON file.
/// </remarks>
public sealed class AppOptions
{
    /// <summary>Name of the configuration section in appsettings.json.</summary>
    public const string SectionName = "LidarAgv";

    /// <summary>
    /// True when started with <c>--simulate</c>: fake LiDAR, and GPIO/LCD actions are only
    /// logged. Set by Program.cs, not by appsettings.json.
    /// </summary>
    public bool Simulate { get; set; }

    /// <summary>Address Kestrel binds to. 0.0.0.0 = every network interface, so the PC can reach it.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>TCP port of the REST API.</summary>
    public int HttpPort { get; set; } = 5000;

    /// <summary>
    /// Where the nonvolatile runtime config is stored. Empty = the default
    /// (<c>/var/lib/lidar-agv/config.json</c> on Linux, <c>data/config.json</c> next to the exe in simulation).
    /// </summary>
    public string ConfigPath { get; set; } = "";

    /// <summary>RPLIDAR serial and filtering settings.</summary>
    public LidarOptions Lidar { get; set; } = new();

    /// <summary>Safety-loop timing and fail-safe settings.</summary>
    public SafetyOptions Safety { get; set; } = new();

    /// <summary>GPIO pin assignment (BCM numbers).</summary>
    public GpioOptions Gpio { get; set; } = new();

    /// <summary>LCD bus and refresh settings.</summary>
    public LcdOptions Lcd { get; set; } = new();

    /// <summary>Settings for the <c>--simulate</c> fake LiDAR.</summary>
    public SimulationOptions Simulation { get; set; } = new();
}

/// <summary>RPLIDAR A1M8 connection and filter settings (from <c>agv.lidar</c> in config.yaml).</summary>
public sealed class LidarOptions
{
    /// <summary>Serial device. <c>/dev/rplidar</c> is the udev symlink from 99-rplidar.rules.</summary>
    public string Port { get; set; } = "/dev/rplidar";

    /// <summary>The A1M8 always talks at 115200 baud, 8N1.</summary>
    public int Baud { get; set; } = 115200;

    /// <summary>The RPLIDAR reports angles clockwise; true converts them to counter-clockwise.</summary>
    public bool Clockwise { get; set; } = true;

    /// <summary>Returns closer than this are ignored: the A1 cannot measure reliably inside ~15 cm.</summary>
    public double MinRangeM { get; set; } = 0.15;

    /// <summary>Returns further than this are ignored (A1M8 rated range is 12 m, but noisy past 8 m).</summary>
    public double MaxRangeM { get; set; } = 8.0;

    /// <summary>Measurements with a quality below this are treated as noise.</summary>
    public int MinQuality { get; set; } = 5;

    /// <summary>Wait between reconnect attempts after a serial/protocol error.</summary>
    public double ReconnectSeconds { get; set; } = 2.0;

    /// <summary>
    /// If more bytes than this are waiting in the serial buffer the host has fallen behind;
    /// the backlog is dropped so the safety logic always sees fresh data.
    /// </summary>
    public int MaxBacklogBytes { get; set; } = 3000;
}

/// <summary>Timing and fail-safe settings of the safety loop.</summary>
public sealed class SafetyOptions
{
    /// <summary>No new revolution within this time = LiDAR fault = red (config.yaml lidar_stale_s).</summary>
    public double StaleTimeoutSeconds { get; set; } = 0.6;

    /// <summary>
    /// A revolution with fewer valid points than this is treated as a fault. WHY: anything
    /// closer than <see cref="LidarOptions.MinRangeM"/> (e.g. a hand over the sensor) produces
    /// no valid returns at all, which would otherwise look like "nothing nearby = CLEAR".
    /// </summary>
    public int MinValidPoints { get; set; } = 20;

    /// <summary>Safety-loop period. 10 ms gives a fast E-stop reaction and smooth LED flashing.</summary>
    public int LoopPeriodMs { get; set; } = 10;

    /// <summary>Consecutive "pressed" samples needed to trip the E-stop (2 x 10 ms = 20 ms).</summary>
    public int EStopTripSamples { get; set; } = 2;

    /// <summary>The button must read "released" continuously this long before a reset is accepted.</summary>
    public int EStopReleaseStableMs { get; set; } = 50;

    /// <summary>
    /// If true the E-stop starts latched after every boot, so a power cut followed by
    /// power-up can never enable motion by itself (restart prevention).
    /// </summary>
    public bool RequireResetAtStartup { get; set; } = true;

    /// <summary>Upper limit for the LED test-mode timeout requested over the API.</summary>
    public int LedTestMaxSeconds { get; set; } = 300;

    /// <summary>Half period of the flashing red LED during an E-stop.</summary>
    public int FlashHalfPeriodMs { get; set; } = 250;
}

/// <summary>GPIO pin assignment. All numbers are BCM (GPIOxx), not physical header pins.</summary>
public sealed class GpioOptions
{
    /// <summary>
    /// gpiochip to use: "auto" finds the Pi 5's RP1 chip by its label (it is gpiochip4 on
    /// older kernels and gpiochip0 on newer ones), or give a number such as "0" or "4".
    /// </summary>
    public string Chip { get; set; } = "auto";

    /// <summary>Green LED (CLEAR). GPIO5 = physical pin 29.</summary>
    public int GreenLedPin { get; set; } = 5;

    /// <summary>Yellow LED (CAUTION). GPIO6 = physical pin 31.</summary>
    public int YellowLedPin { get; set; } = 6;

    /// <summary>Red LED (STOP / E-stop / fault). GPIO13 = physical pin 33.</summary>
    public int RedLedPin { get; set; } = 13;

    /// <summary>E-stop input, NC contact to GND with internal pull-up. GPIO26 = physical pin 37.</summary>
    public int EStopPin { get; set; } = 26;

    /// <summary>Motion-enable output (HIGH = motion allowed). GPIO21 = physical pin 40.</summary>
    public int MotionEnablePin { get; set; } = 21;
}

/// <summary>HD44780 LCD settings that are tied to the wiring (the I2C address is in RuntimeConfig).</summary>
public sealed class LcdOptions
{
    /// <summary>I2C bus number. Header pins 3/5 are bus 1 (<c>/dev/i2c-1</c>).</summary>
    public int I2cBus { get; set; } = 1;

    /// <summary>How often the LCD thread checks whether the text has to change.</summary>
    public int UpdatePeriodMs { get; set; } = 200;

    /// <summary>Wait between attempts to re-initialise a missing/failed LCD.</summary>
    public double RetrySeconds { get; set; } = 5.0;
}

/// <summary>Settings for the fake LiDAR used with <c>--simulate</c>.</summary>
public sealed class SimulationOptions
{
    /// <summary>Simulated revolutions per second (the A1M8 runs at roughly 5.5-10 Hz).</summary>
    public double RateHz { get; set; } = 7.0;

    /// <summary>Beams per revolution.</summary>
    public int Beams { get; set; } = 360;
}
