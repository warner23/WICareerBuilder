#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="$(tr -d '\r\n ' < "$ROOT/VERSION")"
PROJECT="$ROOT/src/WICareerBuilder.Desktop/WICareerBuilder.Desktop.csproj"
ARTIFACTS="$ROOT/artifacts"
PUBLISH_DIR="$ARTIFACTS/publish"

say(){ printf '\nWICareerBuilder v%s\n%s\n' "$VERSION" "$1"; }
fail(){ printf 'ERROR: %s\n' "$1" >&2; exit 1; }
have(){ command -v "$1" >/dev/null 2>&1; }

is_windows_shell(){ [[ "${OS:-}" == "Windows_NT" ]] || have cygpath || [[ "$(uname -s 2>/dev/null || true)" =~ MINGW|MSYS|CYGWIN ]]; }
winpath(){ if have cygpath; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
posix_localappdata(){
  [[ -n "${LOCALAPPDATA:-}" ]] || fail 'LOCALAPPDATA is not available. Run this from Git Bash on Windows.'
  if have cygpath; then cygpath -u "$LOCALAPPDATA"; else printf '%s' "$LOCALAPPDATA"; fi
}
install_dir(){ local base; base="$(posix_localappdata)" || return 1; printf '%s/Programs/WICareerBuilder' "$base"; }
data_dir(){ local base; base="$(posix_localappdata)" || return 1; printf '%s/WICareerBuilder' "$base"; }
installed_exe(){ printf '%s/WICareerBuilder.exe' "$(install_dir)"; }

require_dotnet(){
  have dotnet || fail '.NET SDK not found. Install the .NET 10 SDK first.'
  local major; major="$(dotnet --version | cut -d. -f1)"
  [[ "$major" =~ ^[0-9]+$ ]] || fail 'Could not determine .NET SDK version.'
  (( major >= 10 )) || fail ".NET 10 or newer SDK required; found $(dotnet --version)."
}

cmd_help(){ cat <<'EOF'
Usage: ./wicareer.sh <command> [args]

Development
  doctor              Check the lightweight build environment
  restore             Restore NuGet packages
  build               Build Debug
  build-release       Build Release
  audit               Check NuGet packages for known vulnerabilities
  run                 Build and run from source
  clean               Remove build/artifact output

Database / diagnostics
  database-init       Create/migrate the local SQLite database
  data-path           Show WICareerBuilder personal-data path
  version             Show source version
  status              Show source/install status

Packaging / installation
  publish             Publish Windows x64 framework-dependent build
  package             Create a versioned update ZIP
  install             Publish and install for the current Windows user (shortcuts + Apps entry)
  uninstall           Remove app/shortcuts; preserve personal data
  installer-package   Create customer-facing installer ZIP
  run-installed       Launch installed WICareerBuilder
  demo                Run isolated seeded demo mode
  capture-media       Capture real demo screenshots to release-media
  capture-all-media   Capture screenshots + raw Game Bar demo video
  demo-tour           Run an automatic demo tour for screen recording
  update <zip>        Safely update an existing installation from a package ZIP
  backup              Backup the SQLite database
EOF
}

cmd_doctor(){
  say 'Development environment check'
  is_windows_shell && echo 'Windows/Git Bash: OK' || echo 'Windows/Git Bash: not detected (build may still be inspected here)'
  if have dotnet; then echo ".NET SDK: $(dotnet --version)"; else echo '.NET SDK: MISSING'; fi
  if have powershell.exe || have pwsh; then echo 'PowerShell: OK'; else echo 'PowerShell: MISSING (ZIP packaging needs it on Windows)'; fi
  echo "Source version: $VERSION"
  echo "Install path: $(install_dir 2>/dev/null || echo unavailable)"
  echo "Data path: $(data_dir 2>/dev/null || echo unavailable)"
}

cmd_restore(){ require_dotnet; say 'Restoring packages'; dotnet restore "$ROOT/WICareerBuilder.sln"; }
cmd_build(){ require_dotnet; say 'Building Debug'; dotnet build "$ROOT/WICareerBuilder.sln" -c Debug; }
cmd_build_release(){ require_dotnet; say 'Building Release'; dotnet build "$ROOT/WICareerBuilder.sln" -c Release; }
cmd_audit(){ require_dotnet; say 'Auditing NuGet packages'; dotnet list "$ROOT/WICareerBuilder.sln" package --vulnerable --include-transitive; }
cmd_run(){ require_dotnet; say 'Running from source'; dotnet run --project "$PROJECT"; }
cmd_clean(){ say 'Cleaning output'; find "$ROOT/src" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +; rm -rf "$ARTIFACTS"; }
cmd_database_init(){ require_dotnet; say 'Creating/migrating database'; dotnet run --project "$PROJECT" -- --init-db; echo "Database ready under: $(data_dir)"; }
cmd_version(){ echo "$VERSION"; }
cmd_data_path(){ echo "$(data_dir)"; }

cmd_publish(){
  require_dotnet; is_windows_shell || fail 'Windows/Git Bash is required for the WPF publish target.'
  say 'Publishing Windows x64 build'
  rm -rf "$PUBLISH_DIR"; mkdir -p "$PUBLISH_DIR"
  dotnet publish "$PROJECT" -c Release -r win-x64 --self-contained false -o "$PUBLISH_DIR"
}

powershell_compress(){
  local src="$1" dst="$2"
  local ps='powershell.exe'; have powershell.exe || ps='pwsh'
  "$ps" -NoProfile -Command "Compress-Archive -Path '$(winpath "$src")\\*' -DestinationPath '$(winpath "$dst")' -Force"
}
powershell_expand(){
  local zip="$1" dst="$2"
  local ps='powershell.exe'; have powershell.exe || ps='pwsh'
  "$ps" -NoProfile -Command "Expand-Archive -LiteralPath '$(winpath "$zip")' -DestinationPath '$(winpath "$dst")' -Force"
}

cmd_package(){
  cmd_publish
  say 'Creating update package'
  local stage="$ARTIFACTS/package-stage" zip="$ARTIFACTS/WICareerBuilder_v${VERSION}_win-x64.zip"
  rm -rf "$stage" "$zip"; mkdir -p "$stage"
  cp -a "$PUBLISH_DIR/." "$stage/"
  cp "$ROOT/VERSION" "$stage/VERSION"
  cp "$ROOT/wicareer.sh" "$stage/wicareer.sh"
  mkdir -p "$stage/installer"
  cp "$ROOT/installer/Install-WICareerBuilder.ps1" "$stage/installer/"
  cp "$ROOT/installer/Uninstall-WICareerBuilder.ps1" "$stage/installer/"
  cat > "$stage/update-manifest.json" <<EOF
{
  "product": "WICareerBuilder",
  "version": "$VERSION",
  "architecture": "win-x64",
  "dataPolicy": "Application files replaceable; personal data remains under LocalAppData/WICareerBuilder"
}
EOF
  powershell_compress "$stage" "$zip"
  echo "Package: $zip"
}

cmd_install(){
  cmd_publish
  say 'Installing for current Windows user'
  local stage="$ARTIFACTS/install-stage"
  rm -rf "$stage"; mkdir -p "$stage/installer"
  cp -a "$PUBLISH_DIR/." "$stage/"
  cp "$ROOT/VERSION" "$stage/VERSION"
  cp "$ROOT/wicareer.sh" "$stage/wicareer.sh"
  cp "$ROOT/installer/Install-WICareerBuilder.ps1" "$stage/installer/"
  cp "$ROOT/installer/Uninstall-WICareerBuilder.ps1" "$stage/installer/"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(winpath "$ROOT/installer/Install-WICareerBuilder.ps1")" -SourceDirectory "$(winpath "$stage")" -NoLaunch
  echo "Installed: $(install_dir)"
  echo "Personal data: $(data_dir)"
}

cmd_uninstall(){
  is_windows_shell || fail 'Uninstall is supported from Windows/Git Bash.'
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(winpath "$ROOT/installer/Uninstall-WICareerBuilder.ps1")"
}

cmd_installer_package(){
  cmd_publish
  say 'Creating customer installer package'
  local stage="$ARTIFACTS/installer-stage" zip="$ARTIFACTS/WICareerBuilder_Setup_v${VERSION}_win-x64.zip"
  rm -rf "$stage" "$zip"; mkdir -p "$stage/installer"
  cp -a "$PUBLISH_DIR/." "$stage/"
  cp "$ROOT/VERSION" "$stage/VERSION"
  cp "$ROOT/installer/Install-WICareerBuilder.ps1" "$stage/"
  cp "$ROOT/installer/Uninstall-WICareerBuilder.ps1" "$stage/installer/"
  cp "$ROOT/installer/Install-WICareerBuilder.cmd" "$stage/"
  cp "$ROOT/installer/Uninstall-WICareerBuilder.cmd" "$stage/installer/"
  cp "$ROOT/branding/wicareerbuilder-wordmark.png" "$stage/" 2>/dev/null || true
  powershell_compress "$stage" "$zip"
  echo "Installer package: $zip"
}

process_running(){ tasklist.exe 2>/dev/null | tr -d '\r' | grep -qi '^WICareerBuilder.exe'; }

cmd_update(){
  local zip="${1:-}"; [[ -n "$zip" ]] || fail 'Usage: ./wicareer.sh update <WICareerBuilder_update.zip>'
  [[ -f "$zip" ]] || fail "Update package not found: $zip"
  is_windows_shell || fail 'Updates are supported from Git Bash on Windows.'
  process_running && fail 'Close WICareerBuilder before updating.'

  local dest data stamp backup stage
  dest="$(install_dir)"; data="$(data_dir)"; stamp="$(date +%Y%m%d-%H%M%S)"
  backup="$data/Backups/Updates/$stamp"; stage="$ARTIFACTS/update-stage-$stamp"
  [[ -d "$dest" ]] || fail 'WICareerBuilder is not installed yet. Use ./wicareer.sh install first.'
  rm -rf "$stage"; mkdir -p "$stage" "$backup/app" "$backup/data"
  powershell_expand "$zip" "$stage"
  [[ -f "$stage/WICareerBuilder.exe" ]] || fail 'Invalid update: WICareerBuilder.exe missing.'
  [[ -f "$stage/update-manifest.json" ]] || fail 'Invalid update: update-manifest.json missing.'

  say 'Backing up current application and database'
  cp -a "$dest/." "$backup/app/"
  [[ -f "$data/Data/WICareerBuilder.db" ]] && cp "$data/Data/WICareerBuilder.db" "$backup/data/WICareerBuilder.db"
  [[ -f "$data/Data/WICareerBuilder.db-wal" ]] && cp "$data/Data/WICareerBuilder.db-wal" "$backup/data/WICareerBuilder.db-wal"
  [[ -f "$data/Data/WICareerBuilder.db-shm" ]] && cp "$data/Data/WICareerBuilder.db-shm" "$backup/data/WICareerBuilder.db-shm"

  say 'Applying update'
  if ! { rm -rf "$dest"/* && cp -a "$stage/." "$dest/" && "$dest/WICareerBuilder.exe" --init-db; }; then
    echo 'Update failed; rolling back.' >&2
    rm -rf "$dest"/*; cp -a "$backup/app/." "$dest/"
    if [[ -f "$backup/data/WICareerBuilder.db" ]]; then
      mkdir -p "$data/Data"; cp "$backup/data/WICareerBuilder.db" "$data/Data/WICareerBuilder.db"
      rm -f "$data/Data/WICareerBuilder.db-wal" "$data/Data/WICareerBuilder.db-shm"
    fi
    fail "Rollback completed. Backup retained at $backup"
  fi
  rm -rf "$stage"
  echo "Update complete. Safety backup: $backup"
}

cmd_backup(){
  local data stamp dest; data="$(data_dir)"; stamp="$(date +%Y%m%d-%H%M%S)"; dest="$data/Backups/Manual/$stamp"
  mkdir -p "$dest"
  [[ -f "$data/Data/WICareerBuilder.db" ]] || fail 'Database does not exist yet.'
  cp "$data/Data/WICareerBuilder.db" "$dest/WICareerBuilder.db"
  echo "Backup: $dest/WICareerBuilder.db"
}

cmd_run_installed(){ local exe; exe="$(installed_exe)"; [[ -f "$exe" ]] || fail 'Not installed.'; "$exe" >/dev/null 2>&1 & }
cmd_demo(){ require_dotnet; say 'Running isolated demo mode'; dotnet run --project "$PROJECT" -c Release -- --demo; }
cmd_capture_media(){ require_dotnet; say 'Capturing real product screenshots'; bash "$ROOT/scripts/Product/capture_product_media.sh" "$ROOT/release-media/screenshots"; }
cmd_capture_all_media(){ require_dotnet; say 'Capturing Product V1 screenshots and demo video'; bash "$ROOT/scripts/Product/capture_all_media.sh"; }
cmd_demo_tour(){ require_dotnet; say 'Running automatic demo tour'; dotnet run --project "$PROJECT" -c Release -- --demo --demo-tour; }

cmd_status(){
  say 'Status'
  echo "Source: $ROOT"
  echo "Version: $VERSION"
  local dest; dest="$(install_dir 2>/dev/null || true)"
  if [[ -n "$dest" && -f "$dest/WICareerBuilder.exe" ]]; then echo "Installed: yes ($dest)"; else echo 'Installed: no'; fi
  local db="$(data_dir 2>/dev/null || true)/Data/WICareerBuilder.db"
  [[ -f "$db" ]] && echo "Database: $db" || echo 'Database: not created yet'
}

command_name="${1:-help}"; shift || true
case "$command_name" in
  help|-h|--help) cmd_help;; doctor) cmd_doctor;; restore) cmd_restore;; build) cmd_build;; build-release) cmd_build_release;; audit) cmd_audit;; run) cmd_run;; clean) cmd_clean;;
  database-init) cmd_database_init;; data-path) cmd_data_path;; version) cmd_version;; status) cmd_status;; publish) cmd_publish;; package) cmd_package;; install) cmd_install;; uninstall) cmd_uninstall;; installer-package) cmd_installer_package;; update) cmd_update "$@";; backup) cmd_backup;; run-installed) cmd_run_installed;; demo) cmd_demo;; capture-media) cmd_capture_media;; capture-all-media) cmd_capture_all_media;; demo-tour) cmd_demo_tour;;
  *) fail "Unknown command: $command_name (try ./wicareer.sh help)";;
esac
