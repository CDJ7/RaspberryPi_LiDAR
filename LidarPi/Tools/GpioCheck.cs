using LidarPi.Config;
using LidarPi.Hardware;
using LidarPi.Services;
using Microsoft.Extensions.Options;

namespace LidarPi.Tools;

/// <summary>
/// The <c>--gpio-test</c> mode: an interactive wiring check for the LEDs, the motion-enable
/// output and the E-stop input, run from PuTTY/ssh before the service is installed.
/// </summary>
/// <remarks>
/// WHY a separate tool: when the full service runs, the LEDs follow the safety state and the
/// E-stop is latched at power-up, so a wiring mistake (wrong pin, LED backwards, NC/NO mixed
/// up) is hard to tell apart from correct fail-safe behaviour. This tool drives each output
/// on its own and prints the raw E-stop level, using the same <see cref="IndicatorLed"/>,
/// <see cref="DigitalOutputPin"/> and <see cref="EmergencyStopInput"/> classes as the service,
/// so it also proves those classes work on the real Pi 5. Every output is left LOW at the end,
/// also after Ctrl+C. Stop the service first: two programs cannot own the same pins.
/// </remarks>
public sealed class GpioCheck
{
    private readonly GpioChipLocator _chip;
    private readonly SimulationControl _simulation;
    private readonly GpioOptions _pins;
    private readonly ILogger<GpioCheck> _logger;

    /// <summary>Creates the check with the configured pin numbers.</summary>
    public GpioCheck(GpioChipLocator chip, SimulationControl simulation, IOptions<AppOptions> options, ILogger<GpioCheck> logger)
    {
        _chip = chip;
        _simulation = simulation;
        _pins = options.Value.Gpio;
        _logger = logger;
    }

    /// <summary>Runs the three test steps; Ctrl+C ends early but still switches everything off.</summary>
    /// <param name="estopSeconds">How long to watch the E-stop input in step 3.</param>
    /// <returns>0 if no GPIO error occurred, 1 otherwise.</returns>
    public int Run(int estopSeconds)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };   // finish cleanly instead of dying with pins HIGH
        CancellationToken ct = cancel.Token;

        Console.WriteLine("GPIO wiring test. Stop the service first if it is running (sudo systemctl stop lidar-agv). Ctrl+C ends early.");
        if (!_chip.IsSimulated && _chip.TryGetController(out string? error) is null)
        {
            Console.Error.WriteLine($"GPIO not available: {error}");
            Console.Error.WriteLine("Fixes: sudo apt install -y libgpiod2 (or libgpiod3 on newer images);  add the user to the 'gpio' group and log in again.");
            return 1;
        }
        Console.WriteLine($"GPIO: {(_chip.IsSimulated ? "simulated (nothing is switched)" : _chip.Description)}");

        var green = new IndicatorLed(_chip, _pins.GreenLedPin, "green", _logger);
        var yellow = new IndicatorLed(_chip, _pins.YellowLedPin, "yellow", _logger);
        var red = new IndicatorLed(_chip, _pins.RedLedPin, "red", _logger);
        var motion = new DigitalOutputPin(_chip, _pins.MotionEnablePin, "motion enable", _logger);
        var estop = new EmergencyStopInput(_chip, _pins.EStopPin, _simulation, _logger);
        GpioDevice[] all = [green, yellow, red, motion, estop];
        try
        {
            // Step 1: each LED alone, then all together, so a swapped or reversed LED is obvious.
            Console.WriteLine();
            Console.WriteLine("Step 1 - LEDs (each should light alone for 1.5 s, then all three together):");
            foreach (IndicatorLed led in new[] { green, yellow, red })
            {
                Console.WriteLine($"  {led.Color,-6} LED  GPIO{led.Pin,-2} (physical pin {PhysicalPin(led.Pin)})  -> ON");
                led.Set(true);
                if (Sleep(1500, ct)) return Finish(all);
                led.Set(false);
            }
            Console.WriteLine("  all three ON");
            green.Set(true); yellow.Set(true); red.Set(true);
            if (Sleep(1500, ct)) return Finish(all);
            green.Set(false); yellow.Set(false); red.Set(false);

            // Step 2: motion enable has no LED on it yet, so it is checked with a multimeter.
            Console.WriteLine();
            Console.WriteLine($"Step 2 - motion enable GPIO{motion.Pin} (physical pin {PhysicalPin(motion.Pin)}); measure it against a GND pin:");
            Console.WriteLine("  HIGH for 4 s  -> the meter should read about 3.3 V");
            motion.Set(true);
            if (Sleep(4000, ct)) return Finish(all);
            Console.WriteLine("  LOW  for 4 s  -> the meter should read about 0 V");
            motion.Set(false);
            if (Sleep(4000, ct)) return Finish(all);

            // Step 3: raw E-stop level. Print only changes so presses are easy to see.
            Console.WriteLine();
            Console.WriteLine($"Step 3 - E-stop GPIO{estop.Pin} (physical pin {PhysicalPin(estop.Pin)}) for {estopSeconds} s: press the button, then twist it to release.");
            Console.WriteLine("  Expected: released = LOW (NC contact closed to GND), pressed or wire unplugged = HIGH (STOP).");
            bool? last = null;
            int presses = 0;
            DateTime end = DateTime.UtcNow.AddSeconds(estopSeconds);
            while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
            {
                bool pressed = estop.ReadPressed();
                if (pressed != last)
                {
                    if (pressed && last == false) presses++;
                    Console.WriteLine($"  {DateTime.Now:HH:mm:ss.fff}  {(pressed ? "HIGH  -> PRESSED / wire open (STOP)" : "LOW   -> released (OK)")}");
                    last = pressed;
                }
                Thread.Sleep(10);
            }
            Console.WriteLine($"  presses seen: {presses}");
            if (last == true && presses == 0)
                Console.WriteLine("  Input stayed HIGH the whole time: check the NC contact is wired from the pin to GND (or the button is still latched down).");
            return Finish(all);
        }
        finally
        {
            foreach (GpioDevice d in all) d.Dispose();   // outputs go LOW, pins are released
        }
    }

    /// <summary>Switches outputs off and returns 1 if any device reported a GPIO error.</summary>
    private static int Finish(GpioDevice[] devices)
    {
        foreach (GpioDevice d in devices) if (d is DigitalOutputPin o) o.Set(false);
        var faults = devices.Where(d => d.FaultMessage is not null).Select(d => $"{d.Name}: {d.FaultMessage}").ToList();
        Console.WriteLine();
        if (faults.Count == 0)
        {
            Console.WriteLine("GPIO test finished: no GPIO errors. All outputs are LOW.");
            return 0;
        }
        foreach (string f in faults) Console.Error.WriteLine($"FAULT {f}");
        return 1;
    }

    /// <summary>Sleeps unless Ctrl+C was pressed.</summary>
    /// <returns>True if the user cancelled.</returns>
    private static bool Sleep(int ms, CancellationToken ct) => ct.WaitHandle.WaitOne(ms);

    /// <summary>
    /// Physical header pin of a BCM GPIO number on the 40-pin header, so the printout matches
    /// what the user sees on the board ("?" for numbers that are not on the header).
    /// </summary>
    public static string PhysicalPin(int bcm) => bcm switch
    {
        2 => "3", 3 => "5", 4 => "7", 14 => "8", 15 => "10", 17 => "11", 18 => "12", 27 => "13",
        22 => "15", 23 => "16", 24 => "18", 10 => "19", 9 => "21", 25 => "22", 11 => "23", 8 => "24",
        7 => "26", 0 => "27", 1 => "28", 5 => "29", 6 => "31", 12 => "32", 13 => "33", 19 => "35",
        16 => "36", 26 => "37", 20 => "38", 21 => "40",
        _ => "?",
    };
}
