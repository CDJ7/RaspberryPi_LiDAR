# LiDAR AGV Safety Monitor (Lab 5)

RPLIDAR A1M8 proximity and safety monitor: a C# program on a Raspberry Pi 5 (LiDAR, LEDs,
E-stop, LCD, REST API) and a C# WinForms GUI on a Windows PC.

> The full step-by-step bring-up guide (I2C, systemd service, wiring, troubleshooting) is
> written in stage 6. This page covers working on the project from a **Linux laptop**.

## Folders

| Folder | Contents |
|---|---|
| `LidarPi/` | The Pi program (C#, ASP.NET Core). Also runs on Linux/Windows with `--simulate`. |
| `LidarPC/` | The PC GUI (C#, WinForms; runs on Windows). |
| `tools/linux-viewer/` | Python viewer for the Linux laptop (dev tool, not part of the submission). |
| `WIRING.md` | Pin map and wiring for the LEDs, E-stop, motion enable, LCD and LiDAR. |
| `CLAUDE.md` | Instructions for Claude Code. |

## Working from a Linux laptop

C# does not need Visual Studio: the free .NET 8 SDK provides the `dotnet` command that builds,
runs and publishes everything. Only the WinForms GUI needs Windows to *run*.

### 1. Install the tools (once)

Debian / Ubuntu / Linux Mint / Pop!_OS:

```bash
sudo apt update && sudo apt install -y dotnet-sdk-8.0 python3-tk rsync git
```

Fedora:

```bash
sudo dnf install -y dotnet-sdk-8.0 python3-tkinter rsync git
```

If your distribution has no `dotnet-sdk-8.0` package, use Microsoft's installer:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 && echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
```

Check: `dotnet --version` prints `8.0.x`. VS Code with the **C# Dev Kit** extension is optional.

### 2. Build and try everything without hardware

```bash
dotnet build LidarAgv.sln
```

```bash
dotnet run --project LidarPi -- --simulate
```

In a second terminal:

```bash
python3 tools/linux-viewer/lidar_viewer.py --host localhost
```

Log in with `admin` / `changeme`, press **Reset E-Stop** (the E-stop is latched at every start
on purpose), and watch the simulated person walk toward the robot.

### 3. Deploy to the Pi

Once, so ssh stops asking for the password:

```bash
ssh-copy-id cdj5x@192.168.1.100
```

Then, every time you change the Pi program:

```bash
./deploy.sh --check
```

`deploy.sh` publishes `LidarPi` for `linux-arm64`, copies only the changed files to
`~/lidar-agv` on the Pi (deleting old ones, so a stale version can never run) and runs the
LiDAR check. Use `--run` to start the program in the foreground instead, then watch it with:

```bash
python3 tools/linux-viewer/lidar_viewer.py
```

### 4. Wire and test the LEDs and E-stop

Wire everything as in [`WIRING.md`](WIRING.md) (Pi powered off), make sure the GPIO library is
installed on the Pi (`sudo apt install -y libgpiod2`), then from the laptop:

```bash
./deploy.sh --gpio-test
```

Each LED lights in turn, the motion-enable pin goes HIGH then LOW, and every press/release of the
E-stop button is printed.

### 5. The Windows GUI

`LidarPC` builds on Linux (`dotnet build` checks it compiles) but its window only opens on
Windows. For the demo and the final, run it on a Windows PC (`dotnet run --project LidarPC`,
or Visual Studio) and connect to `192.168.1.100`.
