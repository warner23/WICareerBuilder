#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "${1:-.}" && pwd)"
cd "$ROOT"

pass(){ printf '%-44s PASS\n' "$1"; }
fail(){ printf '%-44s FAIL: %s\n' "$1" "$2" >&2; exit 1; }
section(){ printf '\n=== %s ===\n' "$1"; }

[[ -f WICareerBuilder.sln && -f VERSION && -d src ]] || fail "ROOT-001" "not a WICareerBuilder repository root"
[[ "$(tr -d '\r\n ' < VERSION)" == "0.9.2" ]] || fail "VERSION-001" "expected source version 0.9.2"
pass "VERSION-001"

section "STRUCTURE"
[[ -f tests/WICareerBuilder.Core.Tests/WICareerBuilder.Core.Tests.csproj ]] || fail "TEST-PROJECT-001" "test project missing"
grep -q 'WICareerBuilder.Core.Tests' WICareerBuilder.sln || fail "SOLUTION-TEST-001" "test project is not in solution"
[[ -f docs/EngineeringReport/Changes/ER-CB-0001_M01_Bash_Test_Foundation.md ]] || fail "ENGINEERING-REPORT-001" "Engineering Report missing"
[[ -f global.json ]] || fail "MTP-CONFIG-001" "global.json test-runner configuration missing"
grep -Eq '"runner"[[:space:]]*:[[:space:]]*"Microsoft\.Testing\.Platform"' global.json || fail "MTP-CONFIG-001" "global.json does not select Microsoft.Testing.Platform"
pass "TEST-PROJECT-001"
pass "SOLUTION-TEST-001"
pass "ENGINEERING-REPORT-001"
pass "MTP-CONFIG-001"

section "SOURCE BOUNDARY"
# M01 is test/verification infrastructure only. It must not move the 86% placement policy yet.
grep -q 'const double AutoThreshold = 0.86;' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || fail "PLACEMENT-BASELINE-001" "v0.9.2 placement threshold baseline changed unexpectedly"
grep -q 'AssessContentPlacement' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || fail "PLACEMENT-BASELINE-002" "placement assessment baseline changed unexpectedly"
pass "PLACEMENT-BASELINE-001"
pass "PLACEMENT-BASELINE-002"

section "MIGRATIONS"
MIGRATIONS="$(find src/WICareerBuilder.Data/Migrations -maxdepth 1 -type f -name '*.sql' | wc -l | tr -d ' ')"
[[ "$MIGRATIONS" == "19" ]] || fail "MIGRATION-COUNT-001" "expected 19 migrations, found $MIGRATIONS"
pass "MIGRATION-COUNT-001"

section "SCRIPT SYNTAX"
bash -n wicareer.sh
bash -n scripts/Verification/verify_cb_m01.sh
pass "BASH-SYNTAX-001"

if [[ "${CB_STATIC_ONLY:-0}" == "1" ]]; then
  printf "\n============================================================\n"
  printf " WICAREERBUILDER CB-M01 STATIC GATES: PASS\n"
  printf " .NET compile/test deliberately skipped by CB_STATIC_ONLY=1.\n"
  printf "============================================================\n"
  exit 0
fi

section "DOTNET ENVIRONMENT"
command -v dotnet >/dev/null 2>&1 || fail "DOTNET-001" ".NET SDK not found"
DOTNET_VERSION="$(dotnet --version)"
DOTNET_MAJOR="${DOTNET_VERSION%%.*}"
[[ "$DOTNET_MAJOR" =~ ^[0-9]+$ ]] && (( DOTNET_MAJOR >= 10 )) || fail "DOTNET-001" ".NET 10+ required; found $DOTNET_VERSION"
echo ".NET SDK: $DOTNET_VERSION"
pass "DOTNET-001"

section "RESTORE"
dotnet restore WICareerBuilder.sln
pass "RESTORE-001"

section "FOCUSED M01 TESTS"
dotnet test --project tests/WICareerBuilder.Core.Tests/WICareerBuilder.Core.Tests.csproj -c Release --no-restore --minimum-expected-tests 5
pass "M01-FOCUSED-TESTS-001"

section "FULL RELEASE BUILD"
dotnet build WICareerBuilder.sln -c Release --no-restore --nologo
pass "RELEASE-BUILD-001"

section "FULL TEST REGRESSION"
dotnet test --solution WICareerBuilder.sln -c Release --no-build --minimum-expected-tests 5
pass "FULL-TEST-REGRESSION-001"

section "PACKAGE AUDIT"
# Informative but retained in the log. `dotnet list package --vulnerable` returns non-zero on operational failure.
dotnet list WICareerBuilder.sln package --vulnerable --include-transitive
pass "PACKAGE-AUDIT-001"

printf '\n============================================================\n'
printf ' WICAREERBUILDER CB-M01 BASH/TEST FOUNDATION: PASS\n'
printf ' Production classifier behaviour intentionally unchanged.\n'
printf ' Next engineering batch: extract placement policy/service.\n'
printf '============================================================\n'
