#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
echo '=== REAL SCREENSHOT CAPTURE ==='
bash "$ROOT/scripts/Product/capture_product_media.sh" "$ROOT/release-media/screenshots"
echo '=== DEMO VIDEO CAPTURE ==='
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$ROOT/scripts/Product/record_demo_video.ps1")" -ProjectRoot "$(cygpath -w "$ROOT")" -OutputDirectory "$(cygpath -w "$ROOT/release-media/videos")"
[ -s "$ROOT/release-media/videos/WICareerBuilder_v1.0.0_DemoTour_raw.mp4" ] || { echo 'FAIL: demo MP4 missing' >&2; exit 21; }
sha256sum "$ROOT/release-media/videos/WICareerBuilder_v1.0.0_DemoTour_raw.mp4" > "$ROOT/release-media/videos/SHA256SUMS.txt"
echo 'MEDIA-CAPTURE-ALL-001 PASS'
