// System.Device.I2c (part of NuGet "System.Device.Gpio"): raw access to /dev/i2c-1 for the bus scan.
using System.Device.I2c;
using System.Globalization;
// Iot.Device.CharacterLcd (NuGet "Iot.Device.Bindings"): HD44780 driver + PCF8574 backpack interface.
using Iot.Device.CharacterLcd;
using LidarPi.Config;
using LidarPi.Hardware;
using Microsoft.Extensions.Options;

namespace LidarPi.Tools;

/// <summary>
/// The <c>--lcd-test [--address 0x3F]</c> mode: finds the PCF8574 backpack on the I2C bus and
/// shows test patterns and every message the service uses on the real 16x2 LCD.
/// </summary>
/// <remarks>
/// WHY: when the LCD stays blank in the service it can be the I2C address, the level shifter,
/// the contrast potentiometer or the wiring, and the service only reports "LCD not found". This
/// tool separates those cases: the bus scan proves the wiring and address, the test pattern
/// proves the HD44780 talks, and "can't read the text" means contrast. The messages are produced
/// with the service's own <see cref="LcdDisplayManager.FitLine"/>, so what you see is exactly
/// what the service will show.
/// </remarks>
public sealed class LcdCheck
{
    // PCF8574 answers at 0x20-0x27, PCF8574A at 0x38-0x3F (set by the A0-A2 solder pads).
    private static readonly int[] BackpackAddresses = [.. Enumerable.Range(0x20, 8), .. Enumerable.Range(0x38, 8)];

    private readonly LcdOptions _lcd;
    private readonly ConfigStore _config;
    private readonly bool _simulate;

    /// <summary>Creates the check from the configured bus and address.</summary>
    public LcdCheck(IOptions<AppOptions> options, ConfigStore config)
    {
        _lcd = options.Value.Lcd;
        _simulate = options.Value.Simulate;
        _config = config;
    }

    /// <summary>Runs the scan and the display test.</summary>
    /// <param name="addressOverride">Address from <c>--address</c>, or null to use the configured one.</param>
    /// <returns>0 if the LCD was found and written, 1 otherwise.</returns>
    public int Run(int? addressOverride)
    {
        int configured = _config.Current.LcdI2cAddress;
        int address = addressOverride ?? configured;
        Console.WriteLine($"LCD test on /dev/i2c-{_lcd.I2cBus}, address 0x{address:X2} (configured: 0x{configured:X2}). Stop the service first if it is running.");
        if (_simulate)
        {
            Console.WriteLine("Simulation: no I2C access. The service would show for example:");
            Show(null, "Near: 0.42m 035°", LcdDisplayManager.TextCaution);
            return 0;
        }

        // Step 1: which backpack addresses answer?
        List<int> found;
        try
        {
            found = Scan();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Cannot open /dev/i2c-{_lcd.I2cBus}: {ex.Message}");
            Console.Error.WriteLine("Fixes: enable I2C (sudo raspi-config -> Interface Options -> I2C, reboot); add the user to the 'i2c' group.");
            return 1;
        }
        Console.WriteLine(found.Count == 0
            ? "Step 1 - bus scan: NO backpack answered at 0x20-0x27 / 0x38-0x3F."
            : $"Step 1 - bus scan: backpack found at {string.Join(", ", found.Select(a => $"0x{a:X2}"))}");
        if (found.Count == 0)
        {
            Console.Error.WriteLine("Check: SDA pin 3 -> LV1/HV1 -> SDA, SCL pin 5 -> LV2/HV2 -> SCL, level shifter LV = 3.3 V, HV = 5 V, common GND, backpack VCC = 5 V.");
            return 1;
        }
        if (!found.Contains(address))
        {
            Console.Error.WriteLine($"Nothing at 0x{address:X2}. Run again with --address 0x{found[0]:X2}, and set that address in the GUI (Configuration -> LCD I2C address).");
            return 1;
        }

        // Step 2: write to the display.
        Console.WriteLine();
        Console.WriteLine("Step 2 - display test (watch the LCD; if the backlight is on but no text is visible, turn the contrast potentiometer):");
        try
        {
            using I2cDevice device = I2cDevice.Create(new I2cConnectionSettings(_lcd.I2cBus, address));
            // false = 4-bit mode: the backpack only wires D4-D7 of the HD44780.
            using var lcd = new Lcd1602(LcdInterface.CreateI2c(device, false)) { BacklightOn = true, DisplayOn = true };
            lcd.Clear();
            Show(lcd, "LCD test 16x2 OK", "0123456789ABCDEF", 3000);
            Console.WriteLine("  backlight OFF for 1 s, then ON");
            lcd.BacklightOn = false;
            Thread.Sleep(1000);
            lcd.BacklightOn = true;
            Show(lcd, "Near: 0.42m 035°", LcdDisplayManager.TextClear, 1500);
            Show(lcd, "Near: 0.61m 350°", LcdDisplayManager.TextCaution, 1500);
            Show(lcd, "Near: 0.21m 010°", LcdDisplayManager.TextStop, 1500);
            Show(lcd, "Near: 0.52m 090°", LcdDisplayManager.TextEStop, 1500);
            Show(lcd, "Near: --.--m", LcdDisplayManager.TextLidarFault, 1500);
            Show(lcd, "Shutting down...", "", 1500);
            Show(lcd, "LCD test done", $"address 0x{address:X2}", 0);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"LCD write failed: {ex.Message} (loose SDA/SCL, or the level shifter lost power)");
            return 1;
        }
        Console.WriteLine();
        Console.WriteLine(address == configured
            ? "LCD test finished. The service uses this address already."
            : $"LCD test finished. Set the address to 0x{address:X2} in the GUI (Configuration -> LCD I2C address) so the service uses it.");
        return 0;
    }

    /// <summary>Probes every backpack address with a one-byte read; a device that ACKs is present.</summary>
    private List<int> Scan()
    {
        if (!File.Exists($"/dev/i2c-{_lcd.I2cBus}"))
            throw new IOException($"/dev/i2c-{_lcd.I2cBus} does not exist (I2C not enabled)");
        var found = new List<int>();
        foreach (int a in BackpackAddresses)
        {
            try
            {
                using I2cDevice d = I2cDevice.Create(new I2cConnectionSettings(_lcd.I2cBus, a));
                d.ReadByte();      // throws IOException when nobody acknowledges the address
                found.Add(a);
            }
            catch (IOException)
            {
                // no device at this address
            }
        }
        return found;
    }

    /// <summary>Writes two lines (formatted exactly like the service) and echoes them to the console.</summary>
    private static void Show(Lcd1602? lcd, string line1, string line2, int holdMs = 0)
    {
        string l1 = LcdDisplayManager.FitLine(line1), l2 = LcdDisplayManager.FitLine(line2);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  |{l1.Replace(LcdDisplayManager.DegreeGlyph, '°')}|  |{l2}|"));
        if (lcd is null) return;
        lcd.SetCursorPosition(0, 0);
        lcd.Write(l1);
        lcd.SetCursorPosition(0, 1);
        lcd.Write(l2);
        if (holdMs > 0) Thread.Sleep(holdMs);
    }
}
