#!/usr/bin/env bash
# Builds the submission zip: ../LidarAgv-Lab5.zip containing a top folder "LidarAgv/" with
# LidarPi/ (Pi program) and LidarPC/ (PC GUI) as separate folders, plus the solution and docs.
#
# Only files git tracks or would track (not .gitignored) are included, so bin/, obj/, .vs/, *.user and data/ (which holds a
# password hash) can never end up in the zip. The Python dev viewer (tools/) is left out: it is
# not part of the C# project. NuGet packages are restored from the .csproj files by "dotnet build".
#
#   ./make-zip.sh                     # -> ../LidarAgv-Lab5.zip
#   ./make-zip.sh ~/Desktop/lab5.zip  # custom output path
set -euo pipefail
cd "$(dirname "$0")"
OUT="$(realpath -m "${1:-../LidarAgv-Lab5.zip}")"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "==> Checking that the solution builds"
dotnet build LidarAgv.sln --nologo -v quiet

mkdir -p "$STAGE/LidarAgv"
git ls-files -z --cached --others --exclude-standard | grep -zv '^tools/' | xargs -0 cp --parents -t "$STAGE/LidarAgv"
rm -f "$OUT"
(cd "$STAGE" && zip -qr "$OUT" LidarAgv)
echo "==> Wrote $OUT ($(du -h "$OUT" | cut -f1), $(unzip -Z1 "$OUT" | grep -vc '/$') files)"
