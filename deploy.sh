#!/usr/bin/env bash
# Build the Pi program on this Linux laptop and copy it to the Raspberry Pi (Linux twin of deploy.ps1).
#
#   ./deploy.sh            publish + copy only
#   ./deploy.sh --check    ... then run the LiDAR check on the Pi
#   ./deploy.sh --run      ... then run the program in the foreground (Ctrl+C to stop)
#   ./deploy.sh --gpio-test ... then run the LED / motion-enable / E-stop wiring test
#   ./deploy.sh --lcd-test  ... then scan I2C for the LCD backpack and show test patterns
#   ./deploy.sh --restart  ... then restart the systemd service (after stage 6 is installed)
#
# Settings (override on the command line, e.g.  PI=cdj5x@tikka.local ./deploy.sh):
#   PI    ssh login of the Pi            (default cdj5x@192.168.1.100)
#   DEST  folder in the Pi user's home   (default lidar-agv)
# Tip: run  ssh-copy-id cdj5x@192.168.1.100  once so ssh/rsync stop asking for the password.
set -euo pipefail

PI="${PI:-cdj5x@192.168.1.100}"
DEST="${DEST:-lidar-agv}"
cd "$(dirname "$0")"
OUT="LidarPi/bin/publish/linux-arm64/"

echo "==> Publishing LidarPi for linux-arm64 (self-contained)"
dotnet publish LidarPi -p:PublishProfile=Pi5-linux-arm64 --nologo -v quiet

echo "==> Copying to $PI:~/$DEST"
# rsync only sends changed files (fast after the first copy); --delete removes files of old
# versions so the Pi can never run a stale LidarPi.dll. Falls back to scp if rsync is missing.
if ssh "$PI" command -v rsync >/dev/null 2>&1 && command -v rsync >/dev/null 2>&1; then
  rsync -az --delete --info=stats1 "$OUT" "$PI:$DEST/"
else
  ssh "$PI" "mkdir -p '$DEST'"
  scp -q -r "$OUT"* "$PI:$DEST/"
fi
ssh "$PI" "chmod +x '$DEST/LidarPi'"
echo "==> Deployed. Version on the Pi: $(ssh "$PI" "stat -c '%y' '$DEST/LidarPi.dll'" | cut -d. -f1)"

case "${1:-}" in
  --check)   ssh -t "$PI" "cd '$DEST' && ./LidarPi --check --scans 20" ;;
  --run)     ssh -t "$PI" "cd '$DEST' && ./LidarPi" ;;
  --gpio-test) ssh -t "$PI" "cd '$DEST' && ./LidarPi --gpio-test --seconds 20" ;;
  --lcd-test)  ssh -t "$PI" "cd '$DEST' && ./LidarPi --lcd-test" ;;
  --restart) ssh -t "$PI" "sudo systemctl restart lidar-agv && systemctl --no-pager status lidar-agv | head -15" ;;
  "")        ;;
  *)         echo "Unknown option $1 (use --check, --run, --gpio-test, --lcd-test or --restart)"; exit 1 ;;
esac
