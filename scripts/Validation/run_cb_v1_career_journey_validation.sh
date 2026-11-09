#!/usr/bin/env bash
set -u
ROOT="${1:-$(pwd)}"
cd "$ROOT" || exit 2
exec dotnet run --project tools/WICareerBuilder.V1JourneyValidation/WICareerBuilder.V1JourneyValidation.csproj -c Release -- "$ROOT"
