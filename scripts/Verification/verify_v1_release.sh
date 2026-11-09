#!/usr/bin/env bash
set -u
TARGET="${1:-$(pwd)}"
cd "$TARGET" || exit 2
fail(){ echo "$1" >&2; exit "${2:-1}"; }

echo '=== WICAREERBUILDER V1 RELEASE VERIFIER ==='
[ "$(tr -d '\r\n ' < VERSION)" = "1.0.0" ] || fail 'VERSION-1.0.0 FAIL' 40
grep -Fq '<Version>1.0.0</Version>' Directory.Build.props || fail 'ASSEMBLY-VERSION-1.0.0 FAIL' 41
[ "$(find src/WICareerBuilder.Data/Migrations -maxdepth 1 -name '*.sql' | wc -l | tr -d ' ')" = "27" ] || fail 'MIGRATION-COUNT-0027 FAIL' 42
grep -Fq 'No-web access is enabled for this record; external processing is denied.' src/WICareerBuilder.Core/Classification/PrivacyBoundaryPolicy.cs || fail 'PRIVACY-BOUNDARY FAIL' 43
grep -Fq 'career_evidence_provenance' src/WICareerBuilder.Data/Migrations/0027_evidence_provenance_traceability.sql || fail 'PROVENANCE-SCHEMA FAIL' 44
[ -f docs/Freeze/CB-M16_V1_Critical_Source_Freeze.sha256 ] || fail 'V1-FREEZE-MANIFEST MISSING' 45
sha256sum -c docs/Freeze/CB-M16_V1_Critical_Source_Freeze.sha256 || fail 'V1-FREEZE-HASH FAIL' 46

echo '=== RESTORE ==='
dotnet restore WICareerBuilder.sln || exit $?

echo '=== CORE TESTS ==='
CORE_OUT="$(dotnet test --project tests/WICareerBuilder.Core.Tests/WICareerBuilder.Core.Tests.csproj -c Release --no-restore 2>&1)"; RC=$?; printf '%s\n' "$CORE_OUT"; [ $RC -eq 0 ] || exit $RC
CORE_TOTAL="$(printf '%s\n' "$CORE_OUT" | sed -n 's/^[[:space:]]*total:[[:space:]]*\([0-9][0-9]*\).*/\1/p' | tail -1)"; [ -n "$CORE_TOTAL" ] && [ "$CORE_TOTAL" -ge 62 ] || fail "CORE TEST COUNT FAIL: ${CORE_TOTAL:-unknown}" 47

echo '=== DATA TESTS ==='
DATA_OUT="$(dotnet test --project tests/WICareerBuilder.Data.Tests/WICareerBuilder.Data.Tests.csproj -c Release --no-restore 2>&1)"; RC=$?; printf '%s\n' "$DATA_OUT"; [ $RC -eq 0 ] || exit $RC
DATA_TOTAL="$(printf '%s\n' "$DATA_OUT" | sed -n 's/^[[:space:]]*total:[[:space:]]*\([0-9][0-9]*\).*/\1/p' | tail -1)"; [ -n "$DATA_TOTAL" ] && [ "$DATA_TOTAL" -ge 19 ] || fail "DATA TEST COUNT FAIL: ${DATA_TOTAL:-unknown}" 48

echo '=== DC MOTOR CORPUS ==='
bash scripts/Validation/run_cb_content_corpus_validation.sh || exit $?

echo '=== V1 SCANNER-TO-CAREER JOURNEY ==='
bash scripts/Validation/run_cb_v1_career_journey_validation.sh "$TARGET" || exit $?

echo '=== RELEASE BUILD ==='
dotnet build WICareerBuilder.sln -c Release --no-restore || exit $?
dotnet build tools/WICareerBuilder.ContentValidation/WICareerBuilder.ContentValidation.csproj -c Release --no-restore || exit $?
dotnet build tools/WICareerBuilder.V1JourneyValidation/WICareerBuilder.V1JourneyValidation.csproj -c Release --no-restore || exit $?

echo '=== FULL TEST REGRESSION ==='
dotnet test --solution WICareerBuilder.sln -c Release --no-build || exit $?

echo '=== PACKAGE AUDIT ==='
dotnet list WICareerBuilder.sln package --vulnerable --include-transitive || exit $?

echo '=== WINDOWS V1 PACKAGE ==='
rm -rf artifacts
bash wicareer.sh package || exit $?
PKG="artifacts/WICareerBuilder_v1.0.0_win-x64.zip"
[ -f "$PKG" ] || fail 'V1 PACKAGE MISSING' 49
sha256sum "$PKG" | tee "$PKG.sha256"

echo '============================================================'
echo ' WICAREERBUILDER V1 RELEASE GATE: PASS'
echo ' Version: 1.0.0'
echo " Core tests: $CORE_TOTAL"
echo " Data tests: $DATA_TOTAL"
echo " Package: $TARGET/$PKG"
echo '============================================================'
