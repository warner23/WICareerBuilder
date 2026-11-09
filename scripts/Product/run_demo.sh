#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
dotnet run --project "$ROOT/src/WICareerBuilder.Desktop/WICareerBuilder.Desktop.csproj" -c Release -- --demo
