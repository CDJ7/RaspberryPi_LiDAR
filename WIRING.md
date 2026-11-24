# Wiring guide — LiDAR AGV Safety Monitor (Raspberry Pi 5)

> ⚠️ **Before you start**
> - **Pi GPIO pins are 3.3 V only.** Never connect 5 V to a GPIO pin; it can destroy the pin or the chip.
> - **Wire with the Pi powered off** (`sudo shutdown -h now`, then unplug USB-C).
> - **The E-stop in this project is a software stop.** It switches the motion-enable *signal*, but it is
>   **not** a substitute for a hardwired E-stop that cuts motor power. On a real AGV the button's second NC
>   contact must drop a safety relay/contactor in the motor supply (see the senior-design `WIRING.md`).
>   Releasing the button never restarts anything by itself: an explicit **Reset E-Stop** from the GUI is required.

All pin numbers are configurable in `LidarPi/appsettings.json` (`LidarAgv:Gpio`). "GPIOxx" is the BCM
number used by the software; "pin" is the physical position on the 40-pin header.

## 1. Pin map

| Function | GPIO (BCM) | Physical pin | Direction | Connects to |
|---|---|---|---|---|
| Green LED (CLEAR) | **GPIO5** | **29** | output | 330 Ω → green LED → GND |
| Yellow LED (CAUTION) | **GPIO6** | **31** | output | 330 Ω → yellow LED → GND |
| Red LED (STOP / E-stop / fault) | **GPIO13** | **33** | output | 330 Ω → red LED → GND |
| E-stop input | **GPIO26** | **37** | input, internal pull-up | E-stop **NC** contact → GND (pin 39) |
| Motion enable | **GPIO21** | **40** | output | indicator LED and/or relay/motor-controller enable input |
| I2C SDA (LCD) | GPIO2 | 3 | I2C | level shifter LV1 |
| I2C SCL (LCD) | GPIO3 | 5 | I2C | level shifter LV2 |
| 3.3 V | — | 1 (and 17) | power | level shifter LV |
| 5 V | — | 2 and 4 | power | LCD backpack VCC, level shifter HV |
| GND | — | 6, 9, 14, 20, 25, 30, 34, 39 | ground | everything |
| RPLIDAR A1M8 | — | USB port | USB | CP2102 adapter board (micro-USB **data** cable) |

The chosen pins avoid I2C (GPIO2/3), the UART (GPIO14/15) and SPI, so all of those stay available.

### Header layout (only the pins used here)

```
                     3.3V  (1) (2)  5V  ───────── LCD backpack VCC / level-shifter HV
  level-shifter LV1 ─ SDA  (3) (4)  5V
  level-shifter LV2 ─ SCL  (5) (6)  GND ───────── common ground rail
                            ...
        green LED ─ GPIO5  (29) (30) GND ──────── LED cathodes
       yellow LED ─ GPIO6  (31) (32)
          red LED ─ GPIO13 (33) (34) GND
                           (35) (36)
     E-stop (NC) ─ GPIO26  (37) (38)
     E-stop (NC) ─    GND  (39) (40) GPIO21 ───── motion enable
```

Pin 1 is the corner pin nearest the SD-card slot; odd numbers are on the inner row, even numbers on the edge.

**Tip:** run a breadboard with three rails: **3.3 V** from pin 1, **5 V** from pin 2, **GND** from pin 6.
The Pi has only two 5 V pins, so share them through the rail rather than stacking jumpers.

## 2. LEDs

```
GPIO5 (pin 29) ──[ 330 Ω ]──▶|── GND        (same for GPIO6/pin 31 and GPIO13/pin 33)
                         anode  cathode
                       (long leg) (short leg, flat side)
```

- **Resistor:** 220–330 Ω. With 330 Ω and a typical 2 V LED: (3.3 V − 2.0 V) / 330 Ω ≈ **4 mA**, plenty
  bright and well under what a GPIO should supply (keep each pin below ~8 mA).
- An LED that never lights is almost always **backwards**: swap its legs.
- What the software does (auto mode): CLEAR = green, CAUTION = yellow, STOP = red, LiDAR fault = red,
  **E-stop = red flashing**. LED test mode (GUI → LEDs tab) sets them by hand for a limited time.

## 3. Emergency-stop button (fail-safe input)

Use the button's **NC (normally closed)** contact. On most contact blocks it is marked **NC**, **1-2**
or **11-12** (NO is 3-4 / 13-14). Check with a multimeter: the NC contact **beeps when the button is
released** and opens when pressed.

```
GPIO26 (pin 37) ───────┐
   (internal pull-up    ─┴─  NC contact (closed when released)
    to 3.3 V enabled     ─┬─
    by the software)     │
GND (pin 39) ───────────┘
```

| Button | Contact | Pin level | Software reads |
|---|---|---|---|
| Released | closed | LOW (pulled to GND) | OK |
| Pressed | open | HIGH (pull-up) | **STOP** |
| Wire broken / unplugged | open | HIGH | **STOP** (fail-safe) |
| GPIO cannot be read | — | — | **STOP** (fail-safe) |

- **No external resistor is needed**: the Pi's internal pull-up (~50 kΩ) is enabled in software.
  For long cables in an electrically noisy place, add an external **4.7–10 kΩ pull-up to 3.3 V** and a
  **100 nF** capacitor from the pin to GND.
- **Never** wire the button to 5 V.
- Latching behaviour (software): press → latched immediately (20 ms debounce), red flashing, motion
  enable LOW, LCD shows `!! E-STOP !!`. The latch clears only when the button is **released** (stable for
  50 ms) **and** Reset E-Stop is pressed in the GUI. The E-stop is also latched at every boot, so a power
  cut can never re-enable motion by itself.

## 4. Motion-enable output

GPIO21 (pin 40) is **HIGH (3.3 V) only** when the zone is not STOP, the E-stop is not latched, there is no
LiDAR fault and no shutdown is in progress. It is the signal a future motor controller or relay would use.

- **For the lab:** connect an indicator LED (any colour, e.g. blue/white) with a **330 Ω** resistor exactly
  like the other LEDs, so you can see it switch.
- **Driving a relay:** use a relay **module** with a transistor/opto input rated for 3.3 V logic. Never
  connect a bare relay coil to a GPIO pin.
- **Motor controller enable input:** connect only if it accepts 3.3 V logic; share GND.
- Again: this signal is not a safety function on its own; see the warning at the top.

## 5. HD44780 16x2 LCD with PCF8574 I2C backpack (through a level shifter)

The backpack runs at **5 V** and has pull-up resistors to 5 V on SDA/SCL, so connecting it straight to the
Pi would put 5 V on GPIO2/3. A bidirectional level shifter (e.g. a 4-channel BSS138 board) keeps the
Pi side at 3.3 V.

```
 Raspberry Pi 5                 Level shifter               PCF8574 backpack
 3.3 V (pin 1) ─────────────── LV        HV ──────────────── 5 V (pin 2/4) ── VCC
 GND   (pin 6) ─────────────── GND      GND ──────────────── GND ─────────── GND
 SDA   (pin 3) ─────────────── LV1      HV1 ─────────────────────────────── SDA
 SCL   (pin 5) ─────────────── LV2      HV2 ─────────────────────────────── SCL
```

- **HV = 5 V, LV = 3.3 V, common ground on both sides.** Getting HV and LV the wrong way round is the most
  common mistake.
- Enable I2C once: `sudo raspi-config` → Interface Options → I2C → Yes, then reboot.
- Find the address: `i2cdetect -y 1` (from `sudo apt install -y i2c-tools`). Usually **0x27** or **0x3F**;
  set it in the GUI (Configuration → LCD I2C address) if it is not 0x27.
- **Blank screen or a row of solid blocks:** turn the blue **contrast potentiometer** on the backpack.
- Line 1 shows the closest obstacle (`Near: 0.42m 035°`), line 2 the state (`CLEAR`, `CAUTION - CLOSE`,
  `STOP-TOO CLOSE`, `!! E-STOP !!`, `LIDAR FAULT`).

## 6. RPLIDAR A1M8

- Plug the LiDAR's USB adapter board into a Pi USB port with a micro-USB **data** cable. Charge-only cables
  power the motor (it spins) but `lsusb` shows nothing.
- `lsusb | grep 10c4` must show `Silicon Labs CP210x UART Bridge`; the udev rule
  (`LidarPi/deploy/99-rplidar.rules`) then creates `/dev/rplidar`.
- The motor is switched by the adapter's DTR line; no extra wiring.

## 7. Power

- Use the official **5 V / 5 A** Raspberry Pi 5 supply. With a weaker supply the Pi 5 limits USB current to
  600 mA and the LiDAR motor can stall ("scan data timeout").
- Total extra load in this project: LiDAR ≈ 0.5 A at motor start, LCD backlight ≈ 30–80 mA, LEDs ≈ 4 mA each.

## 8. Testing the wiring

Stop the service if it is running, then run the wiring test on the Pi:

```bash
sudo systemctl stop lidar-agv 2>/dev/null; cd ~/lidar-agv && ./LidarPi --gpio-test --seconds 20
```

1. Green, yellow and red light one at a time (1.5 s each), then all three together.
2. Motion enable (pin 40) goes HIGH for 4 s (multimeter to GND ≈ 3.3 V), then LOW (≈ 0 V).
3. For 20 s every change of the E-stop input is printed: **released = LOW, pressed = HIGH**.
   If it reads HIGH while released, you are on the NO contact or a wire is loose.

For the LCD:

```bash
sudo systemctl stop lidar-agv 2>/dev/null; cd ~/lidar-agv && ./LidarPi --lcd-test
```

1. **Bus scan** lists every backpack address that answers (0x20-0x27, 0x38-0x3F). Nothing found = wiring,
   level shifter (HV/LV swapped, no 5 V) or I2C not enabled. Found at another address = run again with
   `--address 0x3F` (for example) and set that address in the GUI.
2. **Display test**: `LCD test 16x2 OK` / `0123456789ABCDEF`, a backlight blink, then every status message
   the service uses. Backlight on but no readable text = turn the contrast potentiometer.
