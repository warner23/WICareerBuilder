#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

printf '\n=== WICareerBuilder / one-pass GitHub setup ===\n'
printf 'This performs local verification first. Any PUBLIC push requires separate typed approval.\n\n'

if [[ -f .candidate-sha256 ]]; then
  echo 'Checking the exact downloaded source file hashes...'
  sha256sum -c .candidate-sha256 >/dev/null || { echo 'STOP: Source fingerprint mismatch. Do not publish.' >&2; exit 1; }
  echo 'SOURCE MANIFEST: PASS'
fi

echo 'Running Doctor...'
bash scripts/CAREERBUILDER_DOCTOR.sh

echo 'Restoring dependencies and building Release...'
dotnet restore WICareerBuilder.sln
dotnet build WICareerBuilder.sln --configuration Release --no-restore

echo 'Running real Core/Data tests using Microsoft.Testing.Platform...'
TEST_LOG="$(mktemp)"
trap 'rm -f "$TEST_LOG"' EXIT
dotnet test --solution WICareerBuilder.sln --configuration Release --no-build --no-restore | tee "$TEST_LOG"
if ! grep -Eq '^[[:space:]]*total:[[:space:]]*81[[:space:]]*$' "$TEST_LOG" || \
   ! grep -Eq '^[[:space:]]*succeeded:[[:space:]]*81[[:space:]]*$' "$TEST_LOG" || \
   ! grep -Eq '^[[:space:]]*failed:[[:space:]]*0[[:space:]]*$' "$TEST_LOG"; then
  echo 'STOP: Expected exactly 81 successful tests, zero failures. No GitHub action permitted.' >&2
  exit 1
fi
echo 'TECHNICAL VERIFICATION: 81/81 PASS; 0 FAIL.'
echo
cat <<'NOTICE'
PUBLICATION SAFETY HOLD:
  * You must personally and solely own this project, or have written permission.
  * Company-name metadata changes did NOT change legal rights.
  * You must have the right to disclose the icons/artwork and project code.
  * Review README, the staged file list, dependency obligations and Alignerr terms.
  * No real CVs, secrets, personal databases or historical private evidence may be committed.
  * This repository has NO open-source licence for its own code.

If any of the above remains uncertain, stop now; the verified source stays local.
NOTICE

read -r -p 'Type STAGE to prepare a LOCAL git commit, or press Enter to stop: ' STAGE
if [[ "$STAGE" != 'STAGE' ]]; then echo 'Stopped without creating a git commit or GitHub repository.'; exit 0; fi

if [[ ! -d .git ]]; then
  git init -b main || { git init; git branch -M main; }
fi

git add -A
printf '\n=== Files staged for git ===\n'
git diff --cached --name-status
printf '\nReview this list before confirming.\n'
read -r -p 'Type COMMIT to save the local initial commit, or press Enter to stop: ' COMMIT
if [[ "$COMMIT" != 'COMMIT' ]]; then echo 'Stopped with files staged locally; nothing uploaded.'; exit 0; fi
if ! git diff --cached --quiet; then
  git commit -m 'Initial WICareerBuilder personal source snapshot'
fi

echo 'Local git commit prepared. No remote has been changed.'
read -r -p 'Type I CONFIRM PUBLIC to affirm sole ownership, rights, content review and PUBLIC GitHub publication; otherwise press Enter: ' APPROVAL
if [[ "$APPROVAL" != 'I CONFIRM PUBLIC' ]]; then echo 'Stopped before any GitHub upload. Local commit is safe.'; exit 0; fi

if [[ -n "$(git remote)" ]]; then
  echo 'Existing remote detected. Stopping; review git remote -v before any push.' >&2
  git remote -v
  exit 1
fi

if command -v gh >/dev/null 2>&1; then
  if gh auth status >/dev/null 2>&1; then
    echo 'Creating PUBLIC GitHub repo using authenticated GitHub CLI...'
    gh repo create WICareerBuilder --public --source=. --remote=origin --push
    echo 'GitHub repository creation/push command completed. Confirm the displayed URL and browse the files.'
    exit 0
  fi
fi

cat <<'MANUAL'
GitHub CLI is not installed or not authenticated. You can finish in your browser:
  1. Open https://github.com/new and sign into your GitHub account.
  2. Name it WICareerBuilder; choose PUBLIC; do NOT initialise with README or .gitignore.
  3. Click Create repository.
  4. Copy its HTTPS repository URL, then run:
       git remote add origin <PASTE-EXACT-HTTPS-URL>
       git push -u origin main
     (Replace the placeholder with the actual URL without angle brackets.)
No GitHub repository was created by this script.
MANUAL
