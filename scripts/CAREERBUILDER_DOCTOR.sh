#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v pwsh.exe >/dev/null 2>&1; then
  pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$PWD/scripts/CAREERBUILDER_DOCTOR.ps1")"
elif command -v powershell.exe >/dev/null 2>&1; then
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$PWD/scripts/CAREERBUILDER_DOCTOR.ps1")"
else
  echo 'Windows PowerShell not found' >&2; exit 1
fi