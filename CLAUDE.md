# CLAUDE.md — LiDAR AGV Safety Monitor (Lab 5 final project)

Instructions for Claude Code working in this repository. Read this first.

## What this is

Standalone repository `CDJ7/RaspberryPi_LiDAR`. (It started in `CDJ7/Senior-Design` under `C#/LidarAgv`
and was moved here with its history; the senior-design Python code it was ported from lives in that repo
under `apex-agv/agv_pi/lidar/`.)

An RPLIDAR A1M8 proximity/safety monitor for an AGV, built for a course final project.
**The graded deliverable is C#** (the instructor requires it). Do not port the programs to
another language. Python is only used for the throw-away dev tool in `tools/linux-viewer/`.

| Folder | What | Runs on |
|---|---|---|
| `LidarPi/` | Headless ASP.NET Core app: RPLIDAR driver, safety loop, GPIO LEDs/E-stop/motion-enable, HD44780 I2C LCD, Basic-auth REST API | Raspberry Pi 5 (`linux-arm64`), or any OS with `--simulate` |
| `LidarPC/` | WinForms GUI (live plot, object list, commands) | **Windows only** (builds on Linux, cannot run there) |
| `tools/linux-viewer/` | Python/Tkinter viewer using the same REST API — dev aid for the Linux laptop, **not submitted** | Linux |

Original requirements: `docs/Claude_Code_Prompt_RPLIDAR_Lab5.md` and the course proposal table
(mapped in `REQUIREMENTS_MAP.md` once written).

## Environment

- Development machine: **Linux laptop**, no Visual Studio. Use the `dotnet` CLI (.NET 8 SDK).
- Target: Raspberry Pi 5 **`tikka`**, user **`cdj5x`**, IP **192.168.1.100**, Raspberry Pi OS 64-bit (Debian).
  Claude cannot reach the Pi: give the user exact commands to run and wait for the output.
- LiDAR: RPLIDAR A1M8 (model byte 0x18, firmware 1.29) on its CP2102 USB adapter → `/dev/rplidar`
  (udev rule `LidarPi/deploy/99-rplidar.rules`). Needs a micro-USB **data** cable.

## Commands

```bash
dotnet build LidarAgv.sln                                  # must finish with 0 warnings
dotnet run --project LidarPi -- --simulate                 # full Pi app on this laptop, fake LiDAR, API on :5000
dotnet run --project LidarPi -- --check --simulate         # LiDAR self-test against the simulator
python3 tools/linux-viewer/lidar_viewer.py --host localhost # watch the simulator (password: changeme)
./deploy.sh [--check|--run|--restart]                      # publish linux-arm64 and rsync to the Pi
curl -u admin:changeme http://localhost:5000/api/status    # quick API check
```

Default API login: `admin` / `changeme` (stored as a PBKDF2 hash in `data/config.json` when simulating,
`/var/lib/lidar-agv/config.json` on the Pi; delete the file to reset).

## Rules for changes

- **Language level:** `net8.0`, C# 12 (`Directory.Build.props`). Do not use .NET 9+ APIs (e.g. `System.Threading.Lock`).
- **Comments are graded:** every class, method and non-obvious field needs a `///` comment that explains WHY.
  `GenerateDocumentationFile` turns missing public docs into CS1591 warnings. Comment third-party
  libraries at their first `using` (NuGet package name + purpose).
- **Inheritance must be a real "is-a":** Pi: `LidarDevice` → `RpLidarA1` / `SimulatedLidar`;
  `GpioDevice` → `DigitalOutputPin` → `IndicatorLed`, `GpioDevice` → `EmergencyStopInput`.
  PC: `RestClientBase` → `LidarPiClient`. Keep these.
- **Safety logic is fail-safe:** any unknown (no/stale scan, unreadable E-stop pin, exception) means STOP
  and motion enable LOW. E-stop reset only when released AND reset via API. Never weaken this.
- **WinForms Designer compatibility:** keep layout in `MainForm.Designer.cs` (plain assignments only);
  custom controls need a parameterless constructor and no timers/network in `DesignMode`.
- The REST JSON contract is duplicated in `LidarPi/Api/ApiDtos.cs` and `LidarPC/Api/ApiDtos.cs`
  (and read by `tools/linux-viewer`). Change all of them together.
- Test hardware code without hardware: `--simulate`, or a fake A1 on a pseudo-terminal
  (set `LidarAgv__Lidar__Port=<pty>`). Never claim hardware behaviour was verified unless the user ran it on the Pi.

## Build stages (from the original plan)

1. ✅ Pi app in `--simulate` mode with the REST API
2. ✅ PC GUI (WinForms) against the simulator
3. ✅ Real RPLIDAR driver + `--check` (verified on the Pi: info/health OK; scan start-up fix pending user test)
4. ⏳ GPIO LEDs, E-stop, motion enable + `WIRING.md`
5. ⏳ LCD on real hardware
6. ⏳ systemd service, sudoers, `deploy.ps1`, `make-zip.ps1` (+ Linux equivalent), README bring-up guide,
   `REQUIREMENTS_MAP.md`, proposal "Description" text

Check in with the user after each stage.
