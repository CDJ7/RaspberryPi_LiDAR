#!/usr/bin/env bash
# One-time setup of the Raspberry Pi for the LiDAR AGV safety monitor. Run ON THE PI, from the
# program folder, after the first deploy:
#
#   cd ~/lidar-agv && sudo bash deploy/install-service.sh
#
# What it does (every step is safe to repeat):
#   1. installs the system packages the program needs (libgpiod for GPIO, i2c-tools for i2cdetect)
#   2. enables I2C for the LCD
#   3. adds the user to the gpio, i2c and dialout groups
#   4. installs the udev rule that creates /dev/rplidar
#   5. installs the narrow sudoers rule for the REST shutdown command (validated with visudo first)
#   6. installs, enables and starts the systemd service (auto-start at boot)
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run with sudo:  sudo bash deploy/install-service.sh" >&2
  exit 1
fi

APP_USER="${SUDO_USER:-cdj5x}"                       # the user who ran sudo owns and runs the service
APP_DIR="$(cd "$(dirname "$0")/.." && pwd)"          # folder that contains the LidarPi executable
DEPLOY="$APP_DIR/deploy"
[[ -x "$APP_DIR/LidarPi" ]] || chmod +x "$APP_DIR/LidarPi"
echo "User: $APP_USER   Program folder: $APP_DIR"

echo "==> 1/6 Packages"
apt-get update -qq
# libgpiod2 on Raspberry Pi OS Bookworm, libgpiod3 on newer images; i2c-tools gives i2cdetect.
apt-get install -y -qq i2c-tools gpiod
apt-get install -y -qq libgpiod2 2>/dev/null || apt-get install -y -qq libgpiod3

echo "==> 2/6 I2C"
if command -v raspi-config >/dev/null; then
  raspi-config nonint do_i2c 0          # 0 = enable (raspi-config's convention)
fi

echo "==> 3/6 Groups gpio, i2c, dialout for $APP_USER"
for g in gpio i2c dialout; do
  getent group "$g" >/dev/null && usermod -aG "$g" "$APP_USER"
done

echo "==> 4/6 udev rule /dev/rplidar"
install -m 0644 "$DEPLOY/99-rplidar.rules" /etc/udev/rules.d/99-rplidar.rules
udevadm control --reload-rules
udevadm trigger --action=add --subsystem-match=tty

echo "==> 5/6 sudoers rule for the shutdown command"
TMP="$(mktemp)"
sed "s/^cdj5x /$APP_USER /" "$DEPLOY/lidar-agv.sudoers" > "$TMP"
visudo -cf "$TMP"                       # refuse to install a rule that does not parse
install -m 0440 -o root -g root "$TMP" /etc/sudoers.d/lidar-agv
rm -f "$TMP"

echo "==> 6/6 systemd service"
# Rewrite user and folder if they differ from the defaults in the unit file.
sed -e "s#/home/cdj5x/lidar-agv#$APP_DIR#g" \
    -e "s/^User=cdj5x/User=$APP_USER/" \
    -e "s/^Group=cdj5x/Group=$(id -gn "$APP_USER")/" \
    "$DEPLOY/lidar-agv.service" > /etc/systemd/system/lidar-agv.service
systemctl daemon-reload
systemctl enable lidar-agv
systemctl restart lidar-agv
sleep 3
systemctl --no-pager --lines=8 status lidar-agv || true

echo
echo "Done. The service now starts at every boot."
echo "  Logs:     journalctl -u lidar-agv -f"
echo "  Stop:     sudo systemctl stop lidar-agv   (e.g. before --check / --gpio-test / --lcd-test)"
echo "  I2C/groups changes need a reboot once:  sudo reboot"
