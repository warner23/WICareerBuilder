# WICareerBuilder — Personal Edition (source candidate)

**Your Learning · Your Evidence · Your Career**

WICareerBuilder is a local-first Windows desktop application for organising learning materials, engineering projects, skills evidence, qualifications and career-focused CV drafts. The system distinguishes finding a document from actually demonstrating a skill, and keeps sensitive records in a local user-data directory.

## Main capabilities

- WPF/.NET 10 desktop shell with local WebView2 interface
- SQLite databases and versioned schema migrations
- Learning resource discovery, classification and review
- Project workspaces, skills, credentials and traceable evidence
- Master evidence record, multiple CV workspaces and explicit approval boundaries
- Local privacy controls; sensitive records are not included in this repository
- 81 automated Core/Data tests in the latest verified Windows candidate
- Project health checks, JSON engineering state and Windows GitHub Actions CI

## Build and test

Requires **Windows**, **.NET SDK 10**, Git Bash or PowerShell; the WPF UI cannot run natively on Linux.

```bash
dotnet restore WICareerBuilder.sln
dotnet build WICareerBuilder.sln --configuration Release --no-restore
dotnet test --solution WICareerBuilder.sln --configuration Release --no-build --no-restore
bash scripts/CAREERBUILDER_DOCTOR.sh
```

The test command deliberately uses the .NET 10 **Microsoft.Testing.Platform** syntax. The prior isolated G2R2 source passed **81 of 81** tests on Windows; this modified candidate needs its own verification before publication.

To experiment with the CLI, run `bash wicareer.sh help`. Some distribution and media-capture workflows require separately built Windows binaries or third-party tools; no installer executable is shipped in this source candidate.

## Architecture and data boundaries

```text
WPF desktop shell / WebView2 UI
          -> WebBridge
          -> Core, Data (SQLite), Documents, Learning
```

Application and user data are separate: by default personal data is held under `%LOCALAPPDATA%\WICareerBuilder`, outside the source tree. **Do not commit personal CVs, real qualification documents, databases, access tokens or private evidence.**

## Project history and public-source scope

This is a focused source snapshot prepared from the independently verified WICareerBuilder v1.0.0 codebase. It contains application source, tests, fixtures, schema migrations and developer tools. Historical commercial-launch documents, release binaries and personal evidence were not copied into this candidate. An earlier full historical archive remains private. No git history has been reconstructed or fabricated.

The original v1.0.0 project was developed using a separate publishing label; this standalone candidate uses product-neutral build metadata. **Changing metadata does not change ownership or copyright.** Before making this project public or using it with Alignerr, the rights holder must confirm personal sole ownership or appropriate permissions and approve the exact publication files.

## Licence and third-party components

This candidate does **not** grant an open-source licence for the project's own code. All rights are reserved unless the actual copyright holder grants permission separately. NuGet dependencies are governed by their own licence terms; see `docs/DEPENDENCIES.md`.

## Status

**Review candidate, not an approved public release.** See `docs/PUBLICATION_GATE.md` for the remaining owner decisions. No real personal data is intentionally distributed.
