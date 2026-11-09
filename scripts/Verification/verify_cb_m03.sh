#!/usr/bin/env bash
set -u

TARGET="${1:-$PWD}"
TARGET="$(cd "$TARGET" 2>/dev/null && pwd)" || {
  echo "ERROR: Cannot resolve target: ${1:-$PWD}"
  exit 1
}
cd "$TARGET" || exit 1

fail() {
  echo "$1"
  echo "CB-M03 COMPLETE: FAIL (exit ${2:-1})"
  echo "State retained for diagnosis/resume."
  exit "${2:-1}"
}

pass() {
  printf '%-48s PASS\n' "$1"
}

[ -f VERSION ] || fail "VERSION-001 FAIL - VERSION missing"
[ "$(tr -d '\r\n ' < VERSION)" = "0.9.2" ] || fail "VERSION-001 FAIL - expected 0.9.2"
pass "VERSION-001"

echo
echo "=== M03 STRUCTURE / BOUNDARY ==="
[ -f src/WICareerBuilder.Core/Classification/PlacementApplicationPolicy.cs ] || fail "APPLICATION-POLICY-001 FAIL"
grep -q 'public static class PlacementApplicationPolicy' src/WICareerBuilder.Core/Classification/PlacementApplicationPolicy.cs || fail "APPLICATION-POLICY-001 FAIL"
grep -q 'ShouldWriteOrganisation' src/WICareerBuilder.Core/Classification/PlacementApplicationPolicy.cs || fail "APPLICATION-POLICY-001 FAIL"
pass "APPLICATION-POLICY-001"

grep -q 'PlacementApplicationPolicy.Decide(placementDecision, current, provenance.Source)' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || fail "APPLICATION-POLICY-002 FAIL"
grep -q 'switch (applicationDecision.Action)' src/WICareerBuilder.Desktop/Interop/WebBridge.cs || fail "APPLICATION-POLICY-002 FAIL"
pass "APPLICATION-POLICY-002"

if grep -q 'var hasProtectedPlacement =' src/WICareerBuilder.Desktop/Interop/WebBridge.cs; then
  fail "APPLICATION-POLICY-003 FAIL - old direct protected-placement computation remains in WebBridge"
fi
if grep -q 'else if (hasProtectedPlacement)' src/WICareerBuilder.Desktop/Interop/WebBridge.cs; then
  fail "APPLICATION-POLICY-003 FAIL - old protected-placement branch remains in WebBridge"
fi
pass "APPLICATION-POLICY-003"

grep -q 'public const double AutoThreshold = 0.86;' src/WICareerBuilder.Core/Classification/PlacementDecisionPolicy.cs || fail "THRESHOLD-001 FAIL"
pass "THRESHOLD-001"

[ -f tests/WICareerBuilder.Core.Tests/PlacementApplicationPolicyTests.cs ] || fail "TEST-SOURCE-001 FAIL"
[ -f tests/WICareerBuilder.Core.Tests/PlacementAuditRegressionTests.cs ] || fail "TEST-SOURCE-001 FAIL"
grep -q 'ManualDifferentPlacement_IsProtectedAndNeverSelectedForAutomaticWrite' tests/WICareerBuilder.Core.Tests/PlacementApplicationPolicyTests.cs || fail "TEST-SOURCE-001 FAIL"
grep -q 'ThreePointStarter_MapsToDcMachinesStarters' tests/WICareerBuilder.Core.Tests/PlacementAuditRegressionTests.cs || fail "TEST-SOURCE-001 FAIL"
grep -q 'TypesOfDcMotor_PrefersTypesAndApplications_WhenBodyContainsConstructionVocabulary' tests/WICareerBuilder.Core.Tests/PlacementAuditRegressionTests.cs || fail "TEST-SOURCE-001 FAIL"
pass "TEST-SOURCE-001"

MIGRATIONS="$(find src/WICareerBuilder.Data/Migrations -maxdepth 1 -type f -name '*.sql' | wc -l | tr -d ' ')"
[ "$MIGRATIONS" = "19" ] || fail "MIGRATION-COUNT-001 FAIL - expected 19, found $MIGRATIONS"
pass "MIGRATION-COUNT-001"

[ -f global.json ] || fail "MTP-CONFIG-001 FAIL - global.json missing"
grep -q '"runner"[[:space:]]*:[[:space:]]*"Microsoft.Testing.Platform"' global.json || fail "MTP-CONFIG-001 FAIL"
pass "MTP-CONFIG-001"

bash -n scripts/Verification/verify_cb_m03.sh || fail "BASH-SYNTAX-001 FAIL"
pass "BASH-SYNTAX-001"

if [ "${CB_STATIC_ONLY:-0}" = "1" ]; then
  echo
  echo "CB-M03 STATIC-ONLY VERIFICATION: PASS"
  exit 0
fi

echo
echo "=== DOTNET ENVIRONMENT ==="
command -v dotnet >/dev/null 2>&1 || fail "DOTNET-001 FAIL - dotnet not found"
DOTNET_VERSION="$(dotnet --version)"
DOTNET_MAJOR="${DOTNET_VERSION%%.*}"
echo ".NET SDK: $DOTNET_VERSION"
[ "$DOTNET_MAJOR" -ge 10 ] 2>/dev/null || fail "DOTNET-001 FAIL - .NET 10+ required"
pass "DOTNET-001"

echo
echo "=== RESTORE ==="
dotnet restore WICareerBuilder.sln || fail "RESTORE-001 FAIL"
pass "RESTORE-001"

echo
echo "=== FOCUSED M03 TESTS ==="
set +e
TEST_OUTPUT="$(dotnet test --project tests/WICareerBuilder.Core.Tests/WICareerBuilder.Core.Tests.csproj -c Release --no-restore 2>&1)"
TEST_RC=$?
set -e
printf '%s\n' "$TEST_OUTPUT"
[ "$TEST_RC" -eq 0 ] || fail "M03-FOCUSED-TESTS-001 FAIL - focused tests exited $TEST_RC" "$TEST_RC"

DISCOVERED="$(printf '%s\n' "$TEST_OUTPUT" | sed -nE 's/^[[:space:]]*total:[[:space:]]*([0-9]+).*/\1/p' | tail -n 1)"
[ -n "$DISCOVERED" ] || fail "M03-FOCUSED-TESTS-001 FAIL - could not read discovered-test count"
echo "Discovered tests: $DISCOVERED"
[ "$DISCOVERED" -ge 24 ] 2>/dev/null || fail "M03-FOCUSED-TESTS-001 FAIL - expected at least 24 tests, found $DISCOVERED"
pass "M03-FOCUSED-TESTS-001"

echo
echo "=== FULL RELEASE BUILD ==="
dotnet build WICareerBuilder.sln -c Release --no-restore || fail "RELEASE-BUILD-001 FAIL"
pass "RELEASE-BUILD-001"

echo
echo "=== FULL TEST REGRESSION ==="
dotnet test --solution WICareerBuilder.sln -c Release --no-build || fail "FULL-TEST-REGRESSION-001 FAIL"
pass "FULL-TEST-REGRESSION-001"

echo
echo "=== PACKAGE AUDIT ==="
dotnet list WICareerBuilder.sln package --vulnerable --include-transitive || fail "PACKAGE-AUDIT-001 FAIL"
pass "PACKAGE-AUDIT-001"

echo
echo "============================================================"
echo " WICAREERBUILDER CB-M03 DECISION POLICY REGRESSION: PASS"
echo " Manual/protected and prior-automatic rules are now test-locked."
echo " Classifier scope and database schema intentionally unchanged."
echo "============================================================"
echo "CB-M03-VERIFY-001 PASS"
