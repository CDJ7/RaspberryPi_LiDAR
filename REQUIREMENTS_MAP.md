# Requirements map — Lab 5 Final Project

Every row of the "Requirement mapping checklist" in `docs/Lab_5_Proposal.docx`, in the same order,
with where it is implemented and how to demonstrate it.

## Project name

**LiDAR AGV Safety Monitor**

## Description (for the proposal form)

The Raspberry Pi 5 continuously reads an RPLIDAR A1M8 360° laser scanner, finds the closest obstacle
and groups the scan into objects, and sorts the situation into three zones: clear, caution (closer than
0.80 m) and stop (closer than 0.35 m). It shows the zone on green, yellow and red LEDs, drives a
"motion enable" output that a future AGV motor controller would use, and latches a stop when the
emergency-stop button is pressed, until the button is released and the stop is reset from the PC. The
person at the PC uses a Windows Forms program to log in to the Pi's password-protected REST API and
watch a live top-down map of the scan with a list of detected objects; they can reset the E-stop, test
the LEDs, read the E-stop input, send a message to the LCD, change the distances and other settings
(which survive a power cycle), view system information and safely shut the Pi down. The 16x2 LCD shows
the closest obstacle's distance and direction on line 1 (for example `Near: 0.42m 035°`) and the state
on line 2 (`CLEAR`, `CAUTION - CLOSE`, `STOP-TOO CLOSE`, `!! E-STOP !!` or `LIDAR FAULT`), or a custom
message from the PC for a few seconds.

## Checklist

| # | Prog. | Requirement | How it is met | Where | Demonstrate |
|---|---|---|---|---|---|
| 1 | Pi | C# program on the Pi that starts automatically at boot (no keyboard, login or manual command) | systemd service `lidar-agv` (`Type=notify`, `Restart=always`, `After=network-online.target`, runs as `cdj5x` with the gpio/i2c/dialout groups) | `LidarPi/deploy/lidar-agv.service`, `LidarPi/deploy/install-service.sh`, `Program.cs` (`UseSystemd`) | Reboot the Pi, don't log in; the GUI connects. `systemctl status lidar-agv` |
| 2 | Pi | Input peripheral (switch or sensor) | Latching mushroom E-stop button (NC contact, GPIO26, pull-up, debounced, fail-safe) — and the RPLIDAR A1M8 sensor | `Hardware/EmergencyStopInput.cs`, `Safety/EmergencyStopLatch.cs`, `Lidar/RpLidarA1.cs` | Press the button: red LED flashes, banner and LCD show E-STOP |
| 3 | Pi | Output peripheral (LED or other) | Green/yellow/red LEDs (GPIO5/6/13) and the motion-enable output (GPIO21) | `Hardware/DigitalOutputPin.cs` (`DigitalOutputPin`, `IndicatorLed`), `Hardware/LedController.cs`, `Safety/SafetyMonitor.cs` | Walk toward the LiDAR: green → yellow → red |
| 4 | Pi | HD44780 display over I2C with a level shifter | 16x2 HD44780 with PCF8574 backpack on I2C bus 1 through a BSS138 level shifter (HV 5 V / LV 3.3 V), `Iot.Device.CharacterLcd` `Lcd1602` + `LcdInterface.CreateI2c`; own thread, writes only changed lines | `Hardware/LcdDisplayManager.cs`, `WIRING.md` §5, `Tools/LcdCheck.cs` | LCD shows `Near: 0.42m 035°` / zone; `./LidarPi --lcd-test` |
| 5 | Pi | Web server for REST API requiring username and password | ASP.NET Core Kestrel on `0.0.0.0:5000`; HTTP Basic auth on every endpoint; password stored as salted PBKDF2-SHA256 hash | `Program.cs`, `Api/BasicAuthHandler.cs`, `Config/PasswordHasher.cs`, `Config/ConfigStore.cs` | `curl http://192.168.1.100:5000/api/status` → 401; with `-u admin:changeme` → 200 |
| 6 | Pi | REST command that changes an output | `POST /api/leds` — `auto` mode, or `test` mode setting green/yellow/red by hand, back to auto after a timeout | `Api/ApiEndpoints.cs` (`SetLeds`), `Hardware/LedController.cs` (`SetTest`, `SetAuto`) | GUI → LEDs tab → Test, tick LEDs → Apply |
| 7 | Pi | REST command that observes an input and returns its state | `GET /api/estop` — debounced and raw button state, pin level, latch, reason, last trip time, trip count | `Api/ApiEndpoints.cs` (`GetEStop`), `Safety/EmergencyStopLatch.cs` | GUI → E-Stop tab → Read E-stop input (press/release the button between reads) |
| 8 | Pi | REST command that changes the LCD | `POST /api/lcd` `{line1, line2, seconds}` — custom message for N seconds; E-stop/fault/STOP warnings keep priority | `Api/ApiEndpoints.cs`, `Hardware/LcdDisplayManager.cs` (`ShowCustomMessage`) | GUI → LCD tab → Send to LCD |
| 9 | Pi | REST command making a nonvolatile configuration change (survives a power cycle) | `PUT /api/config` (stop/caution distance, hysteresis, sector, angle offset, cluster gap, LCD address) and `PUT /api/config/password`; saved atomically (temp file, fsync, rename) to `/var/lib/lidar-agv/config.json`, reloaded at boot | `Config/ConfigStore.cs`, `Config/RuntimeConfig.cs`, `Api/ApiEndpoints.cs` (`UpdateConfig`) | Change the stop distance, unplug the Pi, power on: GUI → Configuration → Load shows the new value |
| 10 | Pi | REST command for system information: OS version, RAM, uptime, IP | `GET /api/system` — `/etc/os-release`, `MemAvailable` from `/proc/meminfo`, `/proc/uptime`, IPv4 addresses from `NetworkInterface`; plus kernel, CPU temperature, LiDAR model/firmware/health/scan rate, LCD and GPIO state | `Services/SystemInfoService.cs`, `Api/ApiEndpoints.cs` (`GetSystem`) | GUI → System tab → Get system info |
| 11 | Pi | REST command that safely shuts down the Pi | `POST /api/shutdown {confirm:true}` — replies first, then motion enable LOW, LEDs off, `Shutting down...` on the LCD, LiDAR motor stopped, `sudo -n /sbin/shutdown -h now` (narrow sudoers rule allows only that command) | `Services/ShutdownService.cs`, `LidarPi/deploy/lidar-agv.sudoers` | GUI → System tab → Shut down Pi… → Yes |
| 12 | PC | C# program on the PC with a GUI (WinForms) | Windows Forms app: login bar, status banner with Reset E-Stop, live double-buffered GDI+ LiDAR plot (range rings, stop/caution circles, zone colours, closest point, zoom, click to select), object sidebar sorted closest first, one tab per command | `LidarPC/MainForm.cs` + `MainForm.Designer.cs`, `Controls/LidarPlotControl.cs`, `Controls/ObjectListControl.cs` | Run LidarPC and connect |
| 13 | PC | Sends a command to change an output | LEDs tab → Apply → `POST /api/leds` | `MainForm.cs` (`btnApplyLeds_Click`), `Api/LidarPiClient.cs` (`SetLedsTestAsync`, `SetLedsAutoAsync`) | As row 6 |
| 14 | PC | Sends a command to get the state of an input | E-Stop tab → Read E-stop input → `GET /api/estop` (the live banner also shows it 10x per second) | `MainForm.cs` (`btnReadEStop_Click`), `LidarPiClient.GetEStopAsync` | As row 7 |
| 15 | PC | Sends a nonvolatile configuration change | Configuration tab → Save to Pi → `PUT /api/config`; Change login → `PUT /api/config/password` | `MainForm.cs` (`btnSaveConfig_Click`, `btnChangePassword_Click`), `LidarPiClient.UpdateConfigAsync` | As row 9 |
| 16 | PC | Requests system information and displays it | System tab → Get system info → `GET /api/system`, formatted in a text box | `MainForm.cs` (`btnRefreshSystem_Click`, `ShowSystem`), `LidarPiClient.GetSystemAsync` | As row 10 |
| 17 | PC | Sends a command to safely shut down the Pi | System tab → Shut down Pi… → "Are you sure?" (No is the default) → `POST /api/shutdown` | `MainForm.cs` (`btnShutdown_Click`), `LidarPiClient.ShutdownAsync` | As row 11 |
| 18 | Both | Each program: ≥ 2 own classes, one derived from another | **Pi:** `LidarDevice` (abstract) → `RpLidarA1`, `SimulatedLidar`; `GpioDevice` (abstract) → `DigitalOutputPin` → `IndicatorLed`, and `GpioDevice` → `EmergencyStopInput`. **PC:** `RestClientBase` (abstract) → `LidarPiClient` | `Lidar/LidarDevice.cs`, `Lidar/RpLidarA1.cs`, `Lidar/SimulatedLidar.cs`, `Hardware/GpioDevice.cs`, `Hardware/DigitalOutputPin.cs`, `Hardware/EmergencyStopInput.cs`, `LidarPC/Api/RestClientBase.cs`, `LidarPC/Api/LidarPiClient.cs` | See row 19 for the "is-a" reasons |
| 19 | Both | Cohesive classes; inheritance is a genuine "is-a" reuse | Each class has one job (below). The real LiDAR and the simulator both **are** a LiDAR: the service and `--check` use only `LidarDevice`, so `--simulate` needs no special cases, and both reuse the base's open/identify/health/close sequence. An LED, the motion-enable line and the E-stop **are** single GPIO lines: they reuse pin ownership, fault handling, retry, simulation logging and safe release from `GpioDevice`. `LidarPiClient` **is** a REST client: it reuses base URL, Basic auth, JSON, timeouts and error mapping from `RestClientBase` | Class comments in each file explain the reason | — |
| 20 | Both | Every class, method and non-obvious variable has a comment explaining WHY | XML `///` comments on every class, method and property, inline comments on protocol bytes, DTR, hysteresis, fail-safe choices. `GenerateDocumentationFile` makes a missing public comment a compiler warning; the solution builds with **0 warnings** | `Directory.Build.props`, all `.cs` files | `dotnet build LidarAgv.sln` |
| 21 | Both | Consistent naming/formatting; third-party libraries commented at first use | Standard C# conventions (PascalCase types/members, `_camelCase` fields, one class per responsibility). Every file that uses a NuGet package has a comment naming the package and its purpose above the `using`: `System.Device.Gpio` (GPIO, I2C), `Iot.Device.Bindings` (HD44780/PCF8574), `System.IO.Ports` (serial), `Microsoft.Extensions.Hosting.Systemd` (systemd). Packages are also commented in `LidarPi.csproj` | `Hardware/*.cs`, `Lidar/RpLidarA1.cs`, `Tools/LcdCheck.cs`, `Program.cs`, `LidarPi.csproj` | — |
| 22 | Both | One zip, one folder for the PC app and one for the Pi app | `make-zip.sh` / `make-zip.ps1` create `LidarAgv-Lab5.zip` → `LidarAgv/LidarPC/` and `LidarAgv/LidarPi/` (+ solution, README, WIRING, this map) | `make-zip.sh`, `make-zip.ps1` | Run the script, open the zip |
| 23 | Both | All dependencies included so the projects can be built and run | NuGet packages are `PackageReference`s and restore automatically on `dotnet build` / Visual Studio; no other installs needed besides the .NET 8 SDK. The Pi publish is self-contained (includes the .NET runtime). The zip was verified to build from a clean unpack with 0 warnings | `LidarPi/LidarPi.csproj`, `LidarPC/LidarPC.csproj`, `Properties/PublishProfiles/Pi5-linux-arm64.pubxml` | Unzip anywhere, `dotnet build LidarAgv.sln` |

## Class responsibilities (row 19)

**Pi (`LidarPi`)**

| Class | Responsibility |
|---|---|
| `LidarDevice` / `RpLidarA1` / `SimulatedLidar` | LiDAR abstraction / A1M8 serial protocol / fake scene for `--simulate` |
| `LidarService` | Reader thread with automatic reconnect; publishes each processed revolution |
| `ScanProcessor` | Clockwise→counter-clockwise conversion, mounting offset, range/quality filters, monitoring sector |
| `ObjectClusterer` | Splits the scan into objects (distance, bearing, width) |
| `ZoneClassifier` | Closest distance → CLEAR/CAUTION/STOP with hysteresis |
| `EmergencyStopLatch` | Debounce and latch/reset rules of the E-stop |
| `SafetyMonitor` | 10 ms safety loop: fail-safe decisions, motion enable, publishes the safety state |
| `GpioChipLocator` | Finds the Pi 5 gpiochip and owns the shared GPIO controller |
| `GpioDevice` / `DigitalOutputPin` / `IndicatorLed` / `EmergencyStopInput` | One GPIO line each (see row 19) |
| `LedController` | LED patterns for auto and test mode |
| `LcdDisplayManager` | What the LCD shows (priorities, custom messages) and writing it over I2C |
| `ConfigStore` / `RuntimeConfig` / `PasswordHasher` / `AppOptions` | Nonvolatile settings, login check / settings record / password hashing / static settings |
| `SystemInfoService` | OS, RAM, uptime, IP |
| `ShutdownService` | Safe shutdown sequence |
| `BasicAuthHandler` / `ApiEndpoints` | REST authentication / REST endpoints |
| `LatestValue<T>` | Thread-safe newest-value hand-over between threads |
| `LidarCheck` / `GpioCheck` / `LcdCheck` | `--check` / `--gpio-test` / `--lcd-test` bring-up tools |

**PC (`LidarPC`)**

| Class | Responsibility |
|---|---|
| `RestClientBase` / `LidarPiClient` | HTTP + Basic auth + JSON + error mapping / typed calls of this API |
| `ApiException` | One exception type with an operator-readable message |
| `LidarPlotControl` | Real-time GDI+ top-down LiDAR plot |
| `ObjectListControl` | Object sidebar sorted closest first |
| `ZoneColors` | One definition of the zone colours |
| `ConnectionSettings` | Remembers host/port/user (not the password) |
| `MainForm` | Window: polling, banner, command tabs |
