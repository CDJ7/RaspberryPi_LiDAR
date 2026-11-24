# LiDAR AGV Safety Monitor (Lab 5 final project)

A Raspberry Pi 5 reads an **RPLIDAR A1M8** 360° laser scanner, finds the closest obstacle and the
objects around an AGV, and shows the safety state on **green / yellow / red LEDs** and a **16x2 LCD**.
An **emergency-stop button** latches a stop, and a **motion-enable** output is HIGH only while it is
safe to move. A **Windows GUI** shows the live scan and controls the Pi through a password-protected
REST API. Both programs are written in **C# (.NET 8)**.

| Folder / file | Contents |
|---|---|
| `LidarPi/` | Pi program: ASP.NET Core service (LiDAR driver, safety loop, GPIO, LCD, REST API) |
| `LidarPC/` | PC program: Windows Forms GUI |
| `LidarAgv.sln` | Visual Studio solution with both projects |
| `WIRING.md` | Pin map and wiring (LEDs, E-stop, motion enable, LCD + level shifter, LiDAR) |
| `REQUIREMENTS_MAP.md` | Every course requirement and the file/class/endpoint that meets it |
| `LidarPi/deploy/` | systemd service, sudoers rule, udev rule, one-shot Pi installer |
| `deploy.sh` / `deploy.ps1` | Build and copy the Pi program from Linux / Windows |
| `make-zip.sh` / `make-zip.ps1` | Build the submission zip |
| `tools/linux-viewer/` | Python viewer for a Linux laptop (dev tool, not part of the submission) |

Pi used in this guide: host **tikka**, user **cdj5x**, IP **192.168.1.100**. Replace them if yours differ.

---

## Quick start without hardware (simulator)

The Pi program runs on any PC with `--simulate`: a fake LiDAR scene (a person walking toward the
robot), and GPIO/LCD actions are only logged.

**Linux** (needs the .NET 8 SDK, see "Linux laptop" below):

```bash
dotnet run --project LidarPi -- --simulate
```

```bash
python3 tools/linux-viewer/lidar_viewer.py --host localhost
```

**Windows / Visual Studio 2022:** open `LidarAgv.sln`, right-click the solution → **Configure Startup
Projects…** → **Multiple startup projects** → set **LidarPi** and **LidarPC** to **Start** (LidarPi first)
→ OK → **F5**. LidarPi starts with its first launch profile, **Simulate (local)**.

Log in to `localhost`, port `5000`, user `admin`, password `changeme`, then press **Reset E-Stop**
(the E-stop is latched at every start on purpose).

---

## Bring-up on the Raspberry Pi (step by step)

### 0. What you need
- Raspberry Pi 5, Raspberry Pi OS 64-bit, the official **5 V / 5 A** supply (a weaker supply limits USB
  current and the LiDAR motor stalls).
- Everything wired as in [`WIRING.md`](WIRING.md), **with the Pi off**.
- RPLIDAR A1M8 on its USB adapter, connected with a micro-USB **data** cable.
- On the PC/laptop: the .NET 8 SDK. Optional: once, `ssh-copy-id cdj5x@192.168.1.100` so ssh stops asking
  for the password.

### 1. Build the Pi program and copy it to the Pi

Pick one way:

- **Linux laptop:** `./deploy.sh` (publishes and copies only changed files into `~/lidar-agv`).
- **Windows, script:** `.\deploy.ps1` in PowerShell (uses the built-in tar/scp/ssh).
- **Windows, Visual Studio + WinSCP:** right-click **LidarPi** → **Publish…** → profile
  **Pi5-linux-arm64** → **Publish**. Copy **everything inside** `LidarPi\bin\publish\linux-arm64\` with
  WinSCP to `/home/cdj5x/lidar-agv/` (overwrite), then in PuTTY: `chmod +x ~/lidar-agv/LidarPi`.
- **Command line (any OS):** `dotnet publish LidarPi -p:PublishProfile=Pi5-linux-arm64`
  (or `dotnet publish LidarPi -c Release -r linux-arm64 --self-contained true`).

The publish is **self-contained**: the .NET runtime is included, nothing has to be installed on the Pi
for .NET.

### 2. One-time Pi setup (I2C, groups, udev rule, sudoers, systemd service)

Easiest: run the installer that is in the copied folder (from PuTTY on the Pi):

```bash
cd ~/lidar-agv && sudo bash deploy/install-service.sh
```

or from the Linux laptop: `./deploy.sh --install` (Windows: `.\deploy.ps1 -Action install`).
Then reboot once so I2C and the new groups take effect:

```bash
sudo reboot
```

<details>
<summary>The same steps by hand</summary>

```bash
# I2C for the LCD (or: sudo raspi-config -> Interface Options -> I2C -> Yes)
sudo raspi-config nonint do_i2c 0
sudo apt install -y i2c-tools gpiod libgpiod2          # libgpiod3 on newer images
# Hardware groups for the user
sudo usermod -aG gpio,i2c,dialout cdj5x
# udev rule -> /dev/rplidar
sudo cp ~/lidar-agv/deploy/99-rplidar.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
# sudoers rule: allows ONLY "/sbin/shutdown -h now" without a password (checked before installing)
sudo visudo -cf ~/lidar-agv/deploy/lidar-agv.sudoers
sudo install -m 0440 ~/lidar-agv/deploy/lidar-agv.sudoers /etc/sudoers.d/lidar-agv
# systemd service: start at boot, restart on failure
sudo cp ~/lidar-agv/deploy/lidar-agv.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now lidar-agv
sudo reboot
```
</details>

### 3. Check the hardware, piece by piece

The tests need the pins and the LiDAR for themselves, so stop the service first:

```bash
sudo systemctl stop lidar-agv
```

| Check | Command (on the Pi, in `~/lidar-agv`) | Expected |
|---|---|---|
| LCD address | `i2cdetect -y 1` | `27` or `3f` in the table |
| LiDAR device | `ls -l /dev/rplidar` | `/dev/rplidar -> ttyUSB0` |
| LiDAR | `./LidarPi --check --scans 20` | model A1 (0x18), health Good, 5.5–10 Hz; an object 0.5 m to the **left** shows under `left` |
| LEDs / motion enable / E-stop | `./LidarPi --gpio-test` | each LED in turn; pin 40 ≈ 3.3 V then 0 V; E-stop prints LOW released / HIGH pressed |
| LCD | `./LidarPi --lcd-test` | bus scan finds the backpack; test patterns and all messages appear |
| Everything, in the foreground | `./LidarPi` | log in the terminal; Ctrl+C stops it cleanly |

From the Linux laptop these are `./deploy.sh --check`, `--gpio-test`, `--lcd-test`, `--run`.

### 4. Start the service and confirm it auto-starts

```bash
sudo systemctl start lidar-agv
```

```bash
sudo reboot
```

After the reboot (no login needed), from any ssh session:

```bash
systemctl status lidar-agv
```

```bash
journalctl -u lidar-agv -f
```

`active (running)` and log lines such as `LiDAR connected`, `Now listening on: http://0.0.0.0:5000`.
Only warnings: `journalctl -u lidar-agv -p warning`.

### 5. Run the GUI and log in

- **Windows:** run LidarPC (F5 in Visual Studio, or `dotnet run --project LidarPC`), host `192.168.1.100`,
  port `5000`, user `admin`, password `changeme`, **Connect**.
- **Linux laptop:** `python3 tools/linux-viewer/lidar_viewer.py`.

After every boot the banner shows **E-STOP** (power-up latch): check the area, then press **Reset E-Stop**.
Change the default password on the **Configuration** tab (stored as a salted PBKDF2 hash; to reset a
forgotten password delete `/var/lib/lidar-agv/config.json` and restart the service).

### 6. Updating after a code change

`./deploy.sh --restart` (Windows: `.\deploy.ps1 -Action restart`) publishes, copies and restarts the service.

### 7. Remote debugging (optional)

1. On the Pi, install Microsoft's debugger once:

   ```bash
   curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l ~/vsdbg
   ```

2. Deploy from the machine you debug on (the `.pdb` symbols are in the publish output).
3. Attach:
   - **Visual Studio:** **Debug → Attach to Process…** → Connection type **SSH** → target
     `cdj5x@192.168.1.100` → select the **LidarPi** process → **Attach** → code type **Managed (.NET Core for Unix)**.
   - **VS Code:** Run and Debug → **Attach to LidarPi on the Pi (ssh)** → F5 → pick **LidarPi**.

The publish build is Release (optimised): breakpoints work, some local variables show as optimised away.
No extension is required for either.

---

## Linux laptop (no Visual Studio needed)

C# does not need Visual Studio: the .NET 8 SDK builds, runs and publishes everything. Only the WinForms
GUI needs Windows to *run* (it still builds on Linux).

```bash
sudo apt update && sudo apt install -y dotnet-sdk-8.0 python3-tk rsync git     # Debian / Ubuntu
```

```bash
sudo dnf install -y dotnet-sdk-8.0 python3-tkinter rsync git                    # Fedora
```

Other distributions: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0`, then add
`~/.dotnet` to `PATH`. Check with `dotnet --version` (8.0.x). VS Code with **C# Dev Kit** is optional.

## Visual Studio notes (Windows)

- **Designer:** double-click `LidarPC/MainForm.cs`. After one build, `LidarPlotControl` and `ObjectListControl`
  appear in the Toolbox under **LidarPC Components** and can be dragged like normal controls.
- **Launch profiles** (LidarPi): `Simulate (local)`, `Check LiDAR`, `Check LiDAR (simulated)`.
- Reload the solution if files were added outside Visual Studio.

## Submission zip

```bash
./make-zip.sh
```

(Windows: `.\make-zip.ps1`.) Writes `../LidarAgv-Lab5.zip` with `LidarAgv/LidarPi/` and `LidarAgv/LidarPC/`
as separate folders. It contains source only (no `bin/`, `obj/`, passwords); NuGet packages restore
automatically on the first build.

## REST API (all endpoints require HTTP Basic auth)

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/scan` | Downsampled points, objects (closest first), zone, closest distance, status |
| GET | `/api/status` | Zone, E-stop, LiDAR fault, LEDs, LCD text |
| GET / POST | `/api/leds` | LED state / `{mode:"auto"}` or `{mode:"test", green, yellow, red, timeoutSeconds}` |
| GET | `/api/estop` | E-stop input: debounced + raw state, latch, last trip |
| POST | `/api/estop/reset` | Clear the latch (409 while the button is still pressed) |
| GET / POST | `/api/lcd` | LCD text / `{line1, line2, seconds}` custom message |
| GET / PUT | `/api/config` | Thresholds, hysteresis, sector, angle offset, cluster gap, LCD address (saved on the Pi) |
| PUT | `/api/config/password` | `{currentPassword, newPassword, newUsername?}` |
| GET | `/api/system` | OS version, RAM, uptime, IPs, LiDAR model/firmware/health/scan rate, LCD, GPIO |
| POST | `/api/shutdown` | `{confirm:true}`: answer, LEDs off, LCD message, LiDAR stop, OS shutdown |
| POST | `/api/sim` | Simulator only: `{estopPressed, lidarUnplugged}` |

Quick test: `curl -u admin:changeme http://192.168.1.100:5000/api/status`

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| LiDAR not spinning | Weak supply (use 5 V / 5 A); ribbon cable not seated in the LiDAR or adapter. The motor is switched by DTR: it stops when the program opens the port and restarts ~2 s later for scanning. |
| `lsusb` shows no `10c4:ea60`, no `/dev/ttyUSB0` | Charge-only micro-USB cable: use a data cable. If dmesg mentions `brltty`: `sudo apt remove brltty`. |
| `/dev/rplidar not found` but `/dev/ttyUSB0` exists | udev rule not installed: step 2. |
| `permission denied` on the serial port or GPIO | User not in `dialout` / `gpio` / `i2c`: `sudo usermod -aG gpio,i2c,dialout cdj5x`, log out and in (the service gets them via `SupplementaryGroups`). |
| `scan data timeout` | Motor stalled (power) or not spinning; the driver waits 2 s for spin-up and 5 s for the first data. |
| `GPIO unavailable ... libgpiod` | `sudo apt install -y libgpiod2` (or `libgpiod3`). |
| E-stop latched and Reset refused | Button still pressed, wired to the NO contact, or wire open; `./LidarPi --gpio-test` shows the raw level. After every boot the E-stop is latched on purpose. |
| `LCD not found at 0x27` | Wrong address (`i2cdetect -y 1`, then set it in the GUI), I2C disabled, or level shifter HV/LV swapped. |
| LCD backlight on, shows blocks or nothing | Turn the contrast potentiometer on the backpack. |
| GUI cannot connect | Wrong IP (`hostname -I` on the Pi), service not running (`systemctl status lidar-agv`), PC and Pi on different networks, or a firewall blocking port 5000 (`sudo ufw allow 5000/tcp` if ufw is enabled). |
| 401 Wrong username or password | Default is admin / changeme; delete `/var/lib/lidar-agv/config.json` and restart to reset. |
| Shutdown fails with a sudo error | sudoers rule not installed: step 2. |

## Safety note

The E-stop and motion-enable output are a **software** stop. They are not a substitute for a hardwired
emergency stop that cuts motor power (see `WIRING.md`). Any unknown state (no or stale LiDAR data, blocked
sensor, unreadable E-stop pin, program error) is treated as STOP.
