param(
  [string]$SourceDirectory = '',
  [string]$InstallDirectory = '',
  [string]$DataDirectory = '',
  [switch]$NoDesktopShortcut,
  [switch]$NoStartMenuShortcut,
  [switch]$NoRegistry,
  [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
  if (Test-Path (Join-Path $PSScriptRoot 'WICareerBuilder.exe')) { $SourceDirectory = $PSScriptRoot }
  else { $SourceDirectory = Split-Path -Parent $PSScriptRoot }
}
$Product = 'WICareerBuilder'
$InstallDir = if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { Join-Path $env:LOCALAPPDATA 'Programs\WICareerBuilder' } else { $InstallDirectory }
$DataDir = if ([string]::IsNullOrWhiteSpace($DataDirectory)) { Join-Path $env:LOCALAPPDATA 'WICareerBuilder' } else { $DataDirectory }
$Exe = Join-Path $InstallDir 'WICareerBuilder.exe'
$Icon = $Exe

Write-Host "Installing $Product for $env:USERNAME"
Write-Host "Source: $SourceDirectory"
Write-Host "Install: $InstallDir"
Write-Host "Data: $DataDir"

if (!(Test-Path (Join-Path $SourceDirectory 'WICareerBuilder.exe'))) {
  throw "WICareerBuilder.exe not found in $SourceDirectory. Run this installer from the published installer package."
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem -Force $InstallDir -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse
Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $InstallDir -Recurse -Force

$previousDataRoot = $env:WICAREERBUILDER_DATA_ROOT
$env:WICAREERBUILDER_DATA_ROOT = $DataDir
try {
  # WICareerBuilder is a Windows/WPF executable. Windows PowerShell does not
  # reliably populate $LASTEXITCODE when a GUI-subsystem process is invoked
  # directly with &, so wait for the real process and inspect its ExitCode.
  $dbInitProcess = Start-Process -FilePath $Exe -ArgumentList '--init-db' -Wait -PassThru
  if ($null -eq $dbInitProcess) {
    throw 'Database initialisation process did not return a process result.'
  }
  if ($dbInitProcess.ExitCode -ne 0) {
    throw "Database initialisation failed with exit code $($dbInitProcess.ExitCode)."
  }
}
finally {
  if ($null -eq $previousDataRoot) {
    Remove-Item Env:WICAREERBUILDER_DATA_ROOT -ErrorAction SilentlyContinue
  }
  else {
    $env:WICAREERBUILDER_DATA_ROOT = $previousDataRoot
  }
}

$Shell = New-Object -ComObject WScript.Shell
if (!$NoDesktopShortcut) {
  $Desktop = [Environment]::GetFolderPath('Desktop')
  $Shortcut = $Shell.CreateShortcut((Join-Path $Desktop 'WICareerBuilder.lnk'))
  $Shortcut.TargetPath = $Exe
  $Shortcut.WorkingDirectory = $InstallDir
  $Shortcut.IconLocation = "$Icon,0"
  $Shortcut.Description = 'WICareerBuilder — Your Learning · Your Evidence · Your Career'
  $Shortcut.Save()
}
if (!$NoStartMenuShortcut) {
  $StartMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\WICareerBuilder'
  New-Item -ItemType Directory -Force -Path $StartMenu | Out-Null
  $Shortcut = $Shell.CreateShortcut((Join-Path $StartMenu 'WICareerBuilder.lnk'))
  $Shortcut.TargetPath = $Exe
  $Shortcut.WorkingDirectory = $InstallDir
  $Shortcut.IconLocation = "$Icon,0"
  $Shortcut.Description = 'WICareerBuilder'
  $Shortcut.Save()
  $UninstallShortcut = $Shell.CreateShortcut((Join-Path $StartMenu 'Uninstall WICareerBuilder.lnk'))
  $UninstallShortcut.TargetPath = 'powershell.exe'
  $UninstallShortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $InstallDir 'installer\Uninstall-WICareerBuilder.ps1') + '"'
  $UninstallShortcut.WorkingDirectory = $InstallDir
  $UninstallShortcut.IconLocation = "$Icon,0"
  $UninstallShortcut.Save()
}

if (!$NoRegistry) {
  $UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WICareerBuilder'
  New-Item -Path $UninstallKey -Force | Out-Null
  $Version = if (Test-Path (Join-Path $InstallDir 'VERSION')) { (Get-Content (Join-Path $InstallDir 'VERSION') -Raw).Trim() } else { '1.0.0' }
  New-ItemProperty -Path $UninstallKey -Name DisplayName -Value 'WICareerBuilder' -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name DisplayVersion -Value $Version -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name Publisher -Value 'WICareerBuilder' -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name InstallLocation -Value $InstallDir -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name DisplayIcon -Value $Exe -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $InstallDir 'installer\Uninstall-WICareerBuilder.ps1') + '"') -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty -Path $UninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
  
}
Write-Host ''
Write-Host 'INSTALL-WICAREERBUILDER-001 PASS'
Write-Host "Installed: $InstallDir"
Write-Host "Personal data retained separately: $DataDir"
if (!$NoLaunch) { Start-Process $Exe }
