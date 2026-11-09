#!/usr/bin/env bash
set -u
ROOT="${1:-$(cd "$(dirname "$0")/../.." && pwd -P)/tests/Fixtures/ContentPlacement/DCMotor}"
PROJECT_ROOT="$(cd "$(dirname "$0")/../.." && pwd -P)"
EXPECTATIONS="${2:-$ROOT/expectations.tsv}"
REPORT="${3:-$PROJECT_ROOT/validation-output/content-placement-corpus.json}"
cd "$PROJECT_ROOT" || exit 1
dotnet run --project tools/WICareerBuilder.ContentValidation/WICareerBuilder.ContentValidation.csproj -c Release -- --root "$ROOT" --expectations "$EXPECTATIONS" --report "$REPORT"
