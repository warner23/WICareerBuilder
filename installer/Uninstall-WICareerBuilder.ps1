param([switch]$RemovePersonalData)
$ErrorActionPreference='Stop'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\WICareerBuilder'
$DataDir = Join-Path $env:LOCALAPPDATA 'WICareerBuilder'
$DesktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'WICareerBuilder.lnk'
$StartMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\WICareerBuilder'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WICareerBuilder'

Get-Process WICareerBuilder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item $DesktopLink -Force -ErrorAction SilentlyContinue
Remove-Item $StartMenu -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $UninstallKey -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
if ($RemovePersonalData -and (Test-Path $DataDir)) { Remove-Item $DataDir -Recurse -Force }

Write-Host 'UNINSTALL-WICAREERBUILDER-001 PASS'
if (!$RemovePersonalData) { Write-Host "Personal data preserved: $DataDir" }
