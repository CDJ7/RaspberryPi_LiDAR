# Claude Code Prompt: RPLIDAR AGV Safety Monitor (Lab 5 Final Project)

> Paste everything below this line into Claude Code. Attach or copy `Lab_5_Proposal.docx` into the project folder first so Claude Code can read it.

---

You're helping me build my Lab 5 final project: an **RPLIDAR-based proximity and safety monitor for an AGV**. It has two C# programs: one on a **Raspberry Pi 5** and one **Windows WinForms GUI** on my PC. Please read this whole prompt before you write any code. Then give me a short plan: the folder layout, the class list with the inheritance you chose, the REST endpoints, and the GPIO pin map. Wait for my OK before you start building.

## 1. My setup and workflow

- **Pi:** Raspberry Pi 5, Raspberry Pi OS 64-bit (Bookworm). I connect with **PuTTY** (SSH) and copy files with **WinSCP**. Ask me for the Pi's username, hostname and IP; don't assume them.
- **PC:** Windows, with the **.NET SDK** installed. Run `dotnet --version` and target that version (or the newest LTS it supports) in both projects.
- **IDE: Visual Studio (Windows) with the .NET desktop development and ASP.NET workloads.** I write, build and debug in Visual Studio and can install any Visual Studio extension or NuGet package I need. Set everything up so it works naturally in Visual Studio:
  - One solution file, `LidarAgv.sln`, containing **both** projects (`LidarPi` and `LidarPC`). It should open, restore and build with F5 / Ctrl+Shift+B with no extra steps. Add a shared `Directory.Build.props` for common settings (nullable, warnings, language version) if it helps.
  - **WinForms must be editable in the Visual Studio Designer.** Use the standard `MainForm.cs` + `MainForm.Designer.cs` + `.resx` split, put the layout in `InitializeComponent()`, and make custom controls (the LiDAR plot and the object sidebar) designer-safe. That means a parameterless constructor, no network calls or timers starting when `DesignMode` is true, and the controls show up in the Toolbox after a build. I want to drag things around in the Designer without breaking your code.
  - Include **`Properties/launchSettings.json`** for `LidarPi` with profiles such as `Simulate (local)` (`--simulate`, so I can F5 the Pi app on my PC) and `Check LiDAR` (`--check`). Tell me how to set **multiple startup projects** so one F5 runs both the simulated Pi app and the GUI.
  - Include a **folder publish profile** (`Properties/PublishProfiles/Pi5-linux-arm64.pubxml`): Release, `linux-arm64`, self-contained, output to a known folder. Then I can right-click → Publish in Visual Studio and copy that folder to the Pi with WinSCP.
  - Add NuGet packages as `PackageReference` entries in the `.csproj` so Visual Studio restores them automatically. Tell me if any Visual Studio extension would genuinely help (for example for remote debugging), but don't make the build depend on one.
  - **Remote debugging (nice to have):** explain how to debug the app running on the Pi from Visual Studio with **Debug → Attach to Process → Connection type: SSH**. That covers installing `vsdbg` on the Pi, shipping symbols (portable PDBs) in the publish output, and attaching to the running service process.
  - Your workflow: you can edit files and run `dotnet build` / `dotnet publish` / `dotnet test` from the command line, but you can't click around in Visual Studio. When something needs a Visual Studio menu action (Designer, publish button, Attach to Process), give me the exact menu path.
- **Build and deploy:** besides the Visual Studio publish profile, it must also work from the command line: `dotnet publish LidarPi -c Release -r linux-arm64 --self-contained true` (or `-p:PublishProfile=Pi5-linux-arm64`). I copy the output to the Pi with WinSCP and run it there. Also give me an optional `deploy.ps1` that does the same thing with Windows' built-in `scp`/`ssh`, so I don't have to drag files around every time.
- **LiDAR:** Slamtec **RPLIDAR A1M8** over its CP2102 USB adapter (115200 baud, 8N1). This is the same unit as my senior design project. If anything in the code depends on the model, tell me how to confirm it from the `GET_INFO` response.

## 2. Reuse my senior design LiDAR code

My senior design repo is **https://github.com/CDJ7/Senior-Design** (private; I'm signed in to GitHub). Clone it next to this project, or ask me for a local path if the clone fails. That code is **Python**, so this project **ports** it to C#; it does not call it. Read these files first:

| File | What to take from it |
|---|---|
| `apex-agv/agv_pi/lidar/rplidar_driver.py` | The full A1M8 serial protocol: the `A5 xx` requests, the 7-byte response descriptor, 5-byte scan-node decoding (quality, angle_q6/64, distance_q2/4), the check-bit validation with one-byte resync on bad nodes, start-of-revolution detection, flushing the backlog when the host falls behind, health check with reset on "Error", and **the motor is switched by DTR (DTR low = motor on)**. Port it to C# with `System.IO.Ports.SerialPort`. |
| `apex-agv/agv_pi/lidar/lidar_service.py` | Run the driver on its own thread and **reconnect automatically** after an error (`reconnect_s` = 2 s). |
| `apex-agv/agv_pi/lidar/scan.py` | Raw-to-usable conversion. The A1 reports angles **clockwise**. Apply `angle_offset_deg` and filter by `min_range_m` 0.15, `max_range_m` 8.0 and `min_quality` 5. |
| `apex-agv/agv_pi/tools/lidar_check.py` | The orientation check: put an object about 0.5 m to the **left** and it must show up on the left. Build an equivalent `--check` mode into the Pi program. |
| `apex-agv/agv_pi/udev/99-apex-agv.rules` | The udev rule for VID `10c4` / PID `ea60` that creates the `/dev/rplidar` symlink. Reuse it. |
| `apex-agv/config.yaml` (`agv.lidar`, `agv.safety`) | Starting values: stop distance **0.35 m**, slow/caution distance **0.80 m**, front half-angle 35°, **LiDAR stale timeout 0.6 s**. |
| `apex-agv/docs/WIRING.md` | The E-stop design idea: wire the input **fail-safe** (normally closed, open = STOP), and resetting the E-stop must **never** restart anything by itself; it needs an explicit reset. Also: the Pi 5 must be powered from a **5 V / 5 A** supply or USB current is limited and the LiDAR motor stalls. |

Don't modify the senior design repo.

## 3. What the system does

**On the Pi (headless, starts at boot):**
1. Reads the RPLIDAR continuously, one full 360° revolution at a time.
2. Finds the **closest obstacle**. Which points count is set by a configurable sector: default **360°**, with an option for front-only (±35°).
3. Groups the scan into **objects**. Split consecutive points wherever the gap between them is larger than a configurable distance (default 0.15 m), and drop clusters with fewer than about 3 points as noise. For each object, report its closest distance, its bearing (the angle of its closest point) and its approximate width.
4. Picks a **zone** from the closest distance and drives the **LEDs**:
   - 🟢 **Green (CLEAR):** closest distance > caution distance (default 0.80 m)
   - 🟡 **Yellow (CLOSE):** between the stop distance and the caution distance
   - 🔴 **Red (TOO CLOSE, STOP):** closest distance < stop distance (default 0.35 m)
   - Add **hysteresis** (for example 5 cm) so the LEDs don't flicker at a boundary.
   - **Fail-safe:** if the LiDAR is disconnected or no scan has arrived within 0.6 s, go to red and show a LiDAR fault.
5. Drives a **"motion enable" output pin**. It is HIGH only when the zone isn't red, the E-stop isn't active and there's no LiDAR fault. This is the signal a future AGV motor controller or relay would use. Comment clearly that a software stop is **not** a substitute for a hardwired E-stop that cuts motor power.
6. Reads an **emergency stop button** (latching mushroom switch, NC contact) on a GPIO input with a pull-up. Debounce it. When it's pressed (or the wire breaks), **latch E-stop**: red LED flashing, motion enable LOW, and the LCD shows the E-stop. The latch clears **only** when the button is released **and** a reset command arrives over the REST API (from the GUI). Pressing reset while the button is still pressed must be rejected with a clear message.
7. Drives an **HD44780 16x2 LCD over I2C** (PCF8574 backpack on SDA = GPIO2 / pin 3 and SCL = GPIO3 / pin 5, through a **level shifter** because the backpack runs at 5 V):
   - Line 1: closest distance and bearing, for example `Near: 0.42m 035°`
   - Line 2: `CLEAR`, `CAUTION - CLOSE`, `STOP-TOO CLOSE`, `!! E-STOP !!`, or `LIDAR FAULT`
   - A REST command can show a custom message for N seconds, after which the LCD goes back to the live display. Warnings (E-stop, fault) always take priority over custom messages.
   - Update the LCD only when the text changes. I2C writes are slow and cause flicker.
   - Find the address with `i2cdetect -y 1` (usually `0x27` or `0x3F`) and make it configurable.
8. Runs an **ASP.NET Core (Kestrel) web server** on `0.0.0.0:5000` (port configurable) with a **REST API that requires a username and password** on every endpoint (HTTP Basic auth). Store the password in the config file as a salted hash, not plain text, with a default I can change through the API.

**On the PC (WinForms GUI):**
1. A login panel for the Pi's IP or hostname, username and password, with a connection status indicator.
2. A **real-time 2D LiDAR plot**: a custom double-buffered control (GDI+, no heavy charting library) showing scan points around the robot. Include:
   - range rings (for example every 0.5 m, with labels) and a 0° forward marker
   - the caution and stop distances drawn as yellow and red circles
   - points colored by zone, and the closest point highlighted
   - zoom with the mouse wheel
   - updates at about 10 Hz by polling the API without freezing the UI (use `async`/`await` and `HttpClient`)
3. A **sidebar** listing every detected object: ID, distance (m), bearing (°) and width, **sorted closest first** and colored by zone. Clicking an object highlights it on the plot.
4. A large **status banner** showing the zone, the closest distance, and E-stop and LiDAR-fault state, plus a **Reset E-Stop** button.
5. Controls for all the PC-side requirements in section 4 (LED test mode, reading the E-stop input, LCD message, saving thresholds, system info, shutdown with an "Are you sure?" confirmation).

## 4. Course requirements: everything must be covered

Read `Lab_5_Proposal.docx` (in this folder). Every row in its requirement table must be met. Here's how I want them mapped; tell me if you see a better mapping:

| Requirement | How this project meets it |
|---|---|
| Pi C# program auto-starts at boot (no keyboard, login or manual command) | **systemd service** (`lidar-agv.service`, `Restart=always`, `After=network-online.target`), running as my user with the `gpio`, `i2c` and `dialout` groups. Give me the exact `sudo systemctl enable --now` steps. |
| Input peripheral (switch or sensor) | E-stop button on GPIO (plus the RPLIDAR itself) |
| Output peripheral (LED etc.) | Green, yellow and red LEDs, plus the motion-enable pin |
| HD44780 over I2C with a level shifter | PCF8574 backpack, using `Iot.Device.Bindings` (`LcdInterface.CreateI2c` + `Lcd1602`, or equivalent) |
| REST web server with username/password | ASP.NET Core minimal API + Basic auth middleware |
| REST command that **changes an output** | `POST /api/leds`: switch between `auto` mode and `test` mode, where test mode sets green/yellow/red manually (auto mode resumes after a timeout or on command). |
| REST command that **observes an input and returns its state** | `GET /api/estop`: raw button state, latched state, last trip time |
| REST command that **changes the LCD** | `POST /api/lcd`: `{ line1, line2, seconds }` |
| REST command for a **nonvolatile config change** (survives a power cycle) | `GET/PUT /api/config`: stop and caution distances, hysteresis, sector angle, angle offset, LCD I2C address, password change. Save it to a JSON file (for example `/var/lib/lidar-agv/config.json`) **atomically** (write a temp file, then rename) and reload it at boot. |
| REST command for **system info**: OS version, available RAM, uptime, current IP | `GET /api/system`: read `/etc/os-release`, `MemAvailable` from `/proc/meminfo`, `/proc/uptime`, and the IP from `NetworkInterface`. Also include the LiDAR model, firmware, health and scan rate. |
| REST command that **safely shuts down** the Pi | `POST /api/shutdown`: send the response first, then turn off the LEDs, show `Shutting down...` on the LCD, stop the LiDAR motor, then run `sudo /sbin/shutdown -h now`. Give me the narrow sudoers rule that allows only that command for my user. |
| Plus, for the live data | `GET /api/scan` (downsampled points, plus objects, zone and closest distance) and `POST /api/estop/reset` |
| PC GUI program (WinForms) | Section 3 above |
| PC sends: output change, input read, nonvolatile config, system info, shutdown | A button or panel for each one in the GUI |
| **Each program**: at least 2 of my own classes, one deriving from another, with a real "is-a" reason | Suggestions (pick what's cleanest and **explain why the inheritance is real**): **Pi:** an abstract `GpioDevice` base (owns the pin, opens it, implements `IDisposable`) with `IndicatorLed : GpioDevice` and `EmergencyStopInput : GpioDevice`, or an abstract `LidarDevice` with `RpLidarA1 : LidarDevice`. **PC:** a `RestClientBase` (base URL, Basic auth header, JSON, timeouts, error mapping) with `LidarPiClient : RestClientBase`. Don't add inheritance just to tick the box. |
| Cohesive classes | One responsibility each: driver, scan processor and object clustering, zone/safety logic, LED controller, E-stop, LCD display manager, config store, system info, API endpoints. On the PC: API client, plot control, object list, main form. |
| **Every class, method and non-obvious variable has a comment explaining WHY** | XML doc comments (`///`) on every class and method, plus inline comments on anything non-obvious (protocol bytes, DTR logic, hysteresis, fail-safe choices). |
| Consistent naming; **third-party libraries commented at first use** | Standard C# conventions. At the first `using` of each NuGet package, add a comment with the package name and what it's used for (`System.Device.Gpio`, `Iot.Device.Bindings`, `System.IO.Ports`, etc.). |
| One zip, one folder for the PC app and one for the Pi app, with all dependencies included | `LidarAgv/LidarPi/` and `LidarAgv/LidarPC/`, each with its own `.csproj`, plus a top-level `README.md`. The packages must restore from the `.csproj` and build with plain `dotnet build`. Give me a `make-zip.ps1` that zips the source (excluding `bin/` and `obj/`). |

## 5. Hardware: give me a pin map and wiring guide

Propose default BCM pins and make them configurable. For example: LEDs on GPIO 5 / 6 / 13, E-stop input on GPIO 26, motion enable on GPIO 21, I2C on GPIO 2 / 3. Avoid pins I2C already uses. Put these in `WIRING.md`:
- A pin table (BCM number **and** physical pin number)
- LED resistor values for 3.3 V logic (about 220–330 Ω) and the reminder that **Pi GPIO is 3.3 V only**
- E-stop wiring: NC contact between the GPIO and GND with an internal pull-up, so **a broken wire reads as STOP**
- LCD backpack → level shifter → Pi wiring (HV = 5 V, LV = 3.3 V, common ground)
- A note that the Pi 5 needs a 5 V / 5 A supply so the LiDAR motor doesn't stall

**Pi 5 GPIO note:** RPi.GPIO-style approaches don't work on the Pi 5. Use `System.Device.Gpio` with the libgpiod driver, and check the correct gpiochip (`gpioinfo` / `gpiodetect`). Depending on the kernel it's `gpiochip4` or `gpiochip0`, so detect it or make it configurable. Make sure the version of `System.Device.Gpio` you choose supports the Pi 5.

## 6. Engineering expectations

- **Threads:** the LiDAR reader, the safety/LED/LCD loop and Kestrel run independently. Share the latest scan through a thread-safe "latest value" holder (like `core/latest.py` in my repo), not a growing queue.
- **Clean shutdown:** on SIGTERM or when the service stops, turn the LEDs off, stop the LiDAR motor and scan, and clear the LCD.
- **Logging:** use `ILogger` to the console so `journalctl -u lidar-agv -f` shows what's happening.
- **A `--simulate` flag** on the Pi app that generates a fake moving scan (like `sim/sim_lidar.py` in my repo) and logs GPIO/LCD actions instead of touching hardware. I need to be able to run the Pi app **on my Windows PC** with `--simulate` and test the whole GUI before I have the hardware wired.
- **A `--check` flag** that prints device info, health, scan rate and front/left/back/right distances, like `lidar_check.py`.
- **Error handling:** a missing LCD, a missing LiDAR or a GPIO failure must not crash the service. Log it, show the fault, keep the API up, and keep retrying.
- Keep it reasonably simple. This is a class lab, not production, but the safety logic must be fail-safe.

## 7. Deliverables

0. `LidarAgv/LidarAgv.sln`: the Visual Studio solution containing both projects, with a `.gitignore` for Visual Studio/.NET (`bin/`, `obj/`, `.vs/`, `*.user`)
1. `LidarAgv/LidarPi/`: the Pi console/web app (.csproj, source, `appsettings.json`, `Properties/launchSettings.json`, `Properties/PublishProfiles/Pi5-linux-arm64.pubxml`, `lidar-agv.service`, `99-rplidar.rules`, sudoers snippet)
2. `LidarAgv/LidarPC/`: the WinForms app, with Designer-compatible forms (`*.Designer.cs` + `.resx`)
3. `LidarAgv/README.md`: a **step-by-step bring-up guide for my exact workflow**:
   1. Enable I2C (`sudo raspi-config` → Interface Options → I2C), check the LCD with `i2cdetect -y 1`
   2. Add my user to the `gpio`, `i2c` and `dialout` groups
   3. Install the udev rule and confirm `/dev/rplidar` exists
   4. Open `LidarAgv.sln` in Visual Studio, run the `Simulate (local)` profile together with the GUI to check everything works, then **right-click LidarPi → Publish → Pi5-linux-arm64**. Copy the publish folder with WinSCP (say which folder) and run `chmod +x` on the binary.
   5. First manual run from PuTTY with `--check`, then a normal run
   6. Install, enable and start the systemd service, reboot, and confirm it auto-starts (`systemctl status`, `journalctl`)
   7. Run the PC GUI and log in
   8. (Optional) Attach the Visual Studio debugger to the running service over SSH
   9. A troubleshooting section: LiDAR not spinning (DTR/power), permission denied on the serial port or GPIO, wrong I2C address, LCD showing blocks (contrast pot), a firewall or wrong IP blocking the API
4. `WIRING.md` (section 5)
5. `REQUIREMENTS_MAP.md`: every row of the proposal table, with the file/class/endpoint that satisfies it. I'll use this to fill out the proposal form and to show my instructor.
6. Also draft 4–6 sentences I can use for the proposal's **"Description"** field: what the Pi does, what the person at the PC does, and what the LCD shows.

## 8. How to work

- Start with the plan (section intro) and wait for my approval.
- Then build in this order, checking in after each stage:
  1. Pi app in `--simulate` mode with the REST API
  2. PC GUI against the simulator
  3. Real RPLIDAR driver plus `--check`
  4. GPIO LEDs and E-stop
  5. LCD
  6. systemd, packaging and docs
- Make sure **the whole solution builds with no warnings you can't explain** (`dotnet build LidarAgv.sln`) before you tell me a stage is done. Remind me to reload the solution in Visual Studio if you add or rename projects or files outside it.
- You can't reach my Pi directly. When something needs to be run on the Pi, give me the exact commands to paste into PuTTY and tell me what output to expect. I'll paste the results back to you.
