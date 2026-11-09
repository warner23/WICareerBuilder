#!/usr/bin/env bash
set -u
ROOT="$(cd "${1:-.}" && pwd)"
cd "$ROOT" || exit 1
fail(){ echo "$1 FAIL"; exit 1; }
pass(){ printf '%-46s PASS\n' "$1"; }

[[ "$(cat VERSION 2>/dev/null)" == "0.9.2" ]] || fail VERSION-001
pass VERSION-001

echo; echo '=== M02 STRUCTURE / BOUNDARY ==='
[[ -f src/WICareerBuilder.Core/Classification/PlacementDecisionPolicy.cs ]] || fail POLICY-001
pass POLICY-001
grep -q 'public const double AutoThreshold = 0.86;' src/WICareerBuilder.Core/Classification/PlacementDecisionPolicy.cs || fail POLICY-002
pass POLICY-002
grep -q 'PlacementDecisionPolicy.Assess' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || fail BRIDGE-001
grep -q 'PlacementDecisionPolicy.Decide' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || fail BRIDGE-001
pass BRIDGE-001
if grep -q 'AssessContentPlacement' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || grep -q 'AutoThreshold' src/WICareerBuilder.Desktop/Interop/WebBridge.cs; then fail BRIDGE-002; fi
pass BRIDGE-002
[[ -f tests/WICareerBuilder.Core.Tests/PlacementDecisionPolicyTests.cs ]] || fail TEST-SOURCE-001
pass TEST-SOURCE-001
grep -q 'Assess_FourPointStarter_ShortEvidence_RemainsBelowAutomaticSubjectThreshold' tests/WICareerBuilder.Core.Tests/PlacementDecisionPolicyTests.cs || fail TEST-FIXTURE-001
grep -q 'Assert.AreEqual(0.85, confidence.Subject' tests/WICareerBuilder.Core.Tests/PlacementDecisionPolicyTests.cs || fail TEST-FIXTURE-001
pass TEST-FIXTURE-001

count=$(find src/WICareerBuilder.Data/Migrations -maxdepth 1 -type f -name '*.sql' | wc -l | tr -d ' ')
[[ "$count" == "19" ]] || fail MIGRATION-COUNT-001
pass MIGRATION-COUNT-001

grep -q 'Microsoft.Testing.Platform' global.json || fail MTP-CONFIG-001
pass MTP-CONFIG-001
bash -n scripts/Verification/verify_cb_m02.sh || fail BASH-SYNTAX-001
pass BASH-SYNTAX-001

if [[ "${CB_STATIC_ONLY:-0}" == "1" ]]; then
  echo; echo 'CB_STATIC_ONLY=1: runtime .NET gates intentionally skipped.'
  echo 'CB-M02-STATIC-VERIFY-001 PASS'
  exit 0
fi

echo; echo '=== DOTNET ENVIRONMENT ==='
command -v dotnet >/dev/null 2>&1 || fail DOTNET-001
sdk=$(dotnet --version); echo ".NET SDK: $sdk"
[[ "${sdk%%.*}" -ge 10 ]] || fail DOTNET-001
pass DOTNET-001

echo; echo '=== RESTORE ==='
dotnet restore WICareerBuilder.sln || exit 1
pass RESTORE-001

echo; echo '=== FOCUSED M02 TESTS ==='
# Do not assign to TMP/TEMP/TMPDIR here. On Git Bash those variables can be
# inherited/exported to child Windows processes. V1 assigned a log filename to
# TMP, causing MSBuild to interpret that file path as its temp directory.
M02_TEST_OUTPUT="$(dotnet test --project tests/WICareerBuilder.Core.Tests/WICareerBuilder.Core.Tests.csproj -c Release --no-restore 2>&1)"
rc=$?
printf '%s\n' "$M02_TEST_OUTPUT"
[[ $rc -eq 0 ]] || exit $rc
total=$(printf '%s\n' "$M02_TEST_OUTPUT" | grep -E '^[[:space:]]*total:' | tail -1 | sed -E 's/.*total:[[:space:]]*([0-9]+).*/\1/')
[[ "$total" =~ ^[0-9]+$ ]] || fail M02-TEST-DISCOVERY-001
[[ "$total" -ge 13 ]] || fail M02-TEST-DISCOVERY-001
echo "Discovered tests: $total"
pass M02-FOCUSED-TESTS-001

echo; echo '=== FULL RELEASE BUILD ==='
dotnet build WICareerBuilder.sln -c Release --no-restore || exit 1
pass RELEASE-BUILD-001

echo; echo '=== FULL TEST REGRESSION ==='
dotnet test --solution WICareerBuilder.sln -c Release --no-build || exit 1
pass FULL-TEST-REGRESSION-001

echo; echo '=== PACKAGE AUDIT ==='
dotnet list WICareerBuilder.sln package --vulnerable --include-transitive || exit 1
pass PACKAGE-AUDIT-001

echo; echo '============================================================'
echo ' WICAREERBUILDER CB-M02 PLACEMENT EXTRACTION: PASS'
echo ' Classifier scope intentionally unchanged.'
echo '============================================================'
echo 'CB-M02-VERIFY-001 PASS'
