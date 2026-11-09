param([string]$OutputDirectory = '')
$ErrorActionPreference='Stop'
$InstallDir=Join-Path $env:LOCALAPPDATA 'Programs\WICareerBuilder'
$Exe=Join-Path $InstallDir 'WICareerBuilder.exe'
$DesktopLink=Join-Path ([Environment]::GetFolderPath('Desktop')) 'WICareerBuilder.lnk'
$StartLink=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\WICareerBuilder\WICareerBuilder.lnk'
$Key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WICareerBuilder'
if (!(Test-Path $Exe)) { throw "Installed EXE missing: $Exe" }
if (!(Test-Path $DesktopLink)) { throw "Desktop shortcut missing: $DesktopLink" }
if (!(Test-Path $StartLink)) { throw "Start Menu shortcut missing: $StartLink" }
if (!(Test-Path $Key)) { throw 'Windows uninstall registration missing.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory=Join-Path $env:TEMP 'WICareerBuilder-installed-acceptance' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$proc=Start-Process $Exe -ArgumentList @('--demo','--capture-product-media',$OutputDirectory) -PassThru -Wait
if ($proc.ExitCode -ne 0) { throw "Installed demo capture failed with exit code $($proc.ExitCode)." }
if (!(Test-Path (Join-Path $OutputDirectory '01-dashboard.png'))) { throw 'Installed app did not produce acceptance screenshot.' }
Write-Host 'INSTALLED-PRODUCT-ACCEPTANCE-001 PASS'
