<#
.SYNOPSIS
  Build the Pi program on Windows and copy it to the Raspberry Pi (Windows twin of deploy.sh).

.DESCRIPTION
  Uses only tools built into Windows 10/11: dotnet, tar, scp and ssh (OpenSSH client).
  The publish folder is packed into one .tar.gz, copied, and unpacked into a clean folder on
  the Pi, so old files can never be left behind (a stale LidarPi.dll once caused a confusing bug).

.EXAMPLE
  .\deploy.ps1                 # publish + copy
  .\deploy.ps1 -Action check   # ... then run the LiDAR check on the Pi
  .\deploy.ps1 -Action restart # ... then restart the systemd service
  .\deploy.ps1 -Pi cdj5x@tikka.local -Action run
#>
param(
    # ssh login of the Pi.
    [string]$Pi = "cdj5x@192.168.1.100",
    # Program folder in the Pi user's home directory.
    [string]$Dest = "lidar-agv",
    # What to do after copying.
    [ValidateSet("none", "check", "run", "gpio-test", "lcd-test", "install", "restart")]
    [string]$Action = "none"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$out = "LidarPi\bin\publish\linux-arm64"
$tgz = Join-Path $env:TEMP "lidar-agv.tar.gz"

Write-Host "==> Publishing LidarPi for linux-arm64 (self-contained)"
dotnet publish LidarPi -p:PublishProfile=Pi5-linux-arm64 --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "==> Packing and copying to ${Pi}:~/$Dest"
tar -czf $tgz -C $out .
scp -q $tgz "${Pi}:lidar-agv.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp failed (is the Pi on and reachable as $Pi?)" }
# Unpack into a fresh folder and swap it in. data/ is kept in case ConfigPath points there.
ssh $Pi "set -e; rm -rf '$Dest.new'; mkdir '$Dest.new'; tar -xzf lidar-agv.tar.gz -C '$Dest.new'; rm lidar-agv.tar.gz; if [ -d '$Dest/data' ]; then cp -a '$Dest/data' '$Dest.new/'; fi; rm -rf '$Dest'; mv '$Dest.new' '$Dest'; chmod +x '$Dest/LidarPi'"
if ($LASTEXITCODE -ne 0) { throw "unpacking on the Pi failed" }
Remove-Item $tgz
Write-Host "==> Deployed."

switch ($Action) {
    "check"     { ssh -t $Pi "cd '$Dest' && ./LidarPi --check --scans 20" }
    "run"       { ssh -t $Pi "cd '$Dest' && ./LidarPi" }
    "gpio-test" { ssh -t $Pi "cd '$Dest' && ./LidarPi --gpio-test --seconds 20" }
    "lcd-test"  { ssh -t $Pi "cd '$Dest' && ./LidarPi --lcd-test" }
    "install"   { ssh -t $Pi "cd '$Dest' && sudo bash deploy/install-service.sh" }
    "restart"   { ssh -t $Pi "sudo systemctl restart lidar-agv && systemctl --no-pager status lidar-agv | head -15" }
}
