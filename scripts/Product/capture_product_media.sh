#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
OUT="${1:-$ROOT/release-media/screenshots}"
mkdir -p "$OUT"

echo "WICareerBuilder Product Screenshot Capture"
echo "Source: $ROOT"
echo "Output: $OUT"

dotnet run --project "$ROOT/src/WICareerBuilder.Desktop/WICareerBuilder.Desktop.csproj" -c Release -- --demo --reset-demo --capture-product-media "$OUT"

expected=(01-dashboard.png 02-projects.png 03-skills.png 04-evidence.png 05-cvs.png 06-portfolio.png 07-about.png CAPTURE_COMPLETE.txt)
for f in "${expected[@]}"; do
  [ -s "$OUT/$f" ] || { echo "FAIL missing/empty $f" >&2; exit 21; }
done
sha256sum "$OUT"/*.png > "$OUT/SHA256SUMS.txt"
echo "MEDIA-SCREENSHOTS-001 PASS"
