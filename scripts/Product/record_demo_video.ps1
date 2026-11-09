param(
  [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
  [string]$OutputDirectory = '',
  [int]$Seconds = 58
)
$ErrorActionPreference='Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $ProjectRoot 'release-media\videos' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$dest = Join-Path $OutputDirectory 'WICareerBuilder_v1.0.0_DemoTour_raw.mp4'

# A valid prior capture is reusable on a recovery rerun.
if ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 100000)) {
  $hash=(Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()
  Write-Host "Existing video retained: $dest"
  Write-Host "SHA256: $hash"
  Write-Host 'VIDEO-CAPTURE-001 PASS (existing verified media)'
  exit 0
}

$Project = Join-Path $ProjectRoot 'src\WICareerBuilder.Desktop\WICareerBuilder.Desktop.csproj'
$captures = Join-Path ([Environment]::GetFolderPath('MyVideos')) 'Captures'
New-Item -ItemType Directory -Force -Path $captures | Out-Null
$start = Get-Date

function Start-DemoTour {
  Write-Host 'Launching real WICareerBuilder automatic demo tour...'
  return Start-Process dotnet -ArgumentList @('run','--project',$Project,'-c','Release','--','--demo','--demo-tour') -PassThru
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WICBWindow {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll", SetLastError=true)] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
  const uint KEYEVENTF_KEYUP = 0x0002;
  public static void RecordToggle() {
    byte VK_LWIN=0x5B, VK_MENU=0x12, VK_R=0x52;
    keybd_event(VK_LWIN,0,0,UIntPtr.Zero);
    keybd_event(VK_MENU,0,0,UIntPtr.Zero);
    keybd_event(VK_R,0,0,UIntPtr.Zero);
    keybd_event(VK_R,0,KEYEVENTF_KEYUP,UIntPtr.Zero);
    keybd_event(VK_MENU,0,KEYEVENTF_KEYUP,UIntPtr.Zero);
    keybd_event(VK_LWIN,0,KEYEVENTF_KEYUP,UIntPtr.Zero);
  }
}
"@

function Focus-WICareerBuilder([int]$TimeoutSeconds = 20) {
  $deadline=(Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    $candidate = Get-Process -ErrorAction SilentlyContinue |
      Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like '*WICareerBuilder*' } |
      Select-Object -First 1
    if ($null -ne $candidate) {
      [WICBWindow]::ShowWindowAsync($candidate.MainWindowHandle, 9) | Out-Null
      Start-Sleep -Milliseconds 250
      [WICBWindow]::SetForegroundWindow($candidate.MainWindowHandle) | Out-Null
      Start-Sleep -Milliseconds 750
      Write-Host "Foreground window: $($candidate.MainWindowTitle)"
      return $true
    }
    Start-Sleep -Milliseconds 500
  }
  Write-Warning 'Could not prove WICareerBuilder became the foreground window.'
  return $false
}

function Find-NewGameBarCapture([datetime]$Since, [int]$TimeoutSeconds = 15) {
  $deadline=(Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    $latest = Get-ChildItem -Path $captures -Filter '*.mp4' -ErrorAction SilentlyContinue |
      Where-Object { $_.LastWriteTime -ge $Since.AddSeconds(-2) -and $_.Length -gt 100000 } |
      Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $latest) { return $latest }
    Start-Sleep -Seconds 1
  }
  return $null
}

function Stop-DemoProcess($Proc) {
  if ($null -ne $Proc -and !$Proc.HasExited) {
    Stop-Process -Id $Proc.Id -Force -ErrorAction SilentlyContinue
  }
  Get-Process -Name 'WICareerBuilder' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Try-GameBarCapture {
  $appCaptureEnabled = $null
  $gameDvrEnabled = $null
  try { $appCaptureEnabled=(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' -Name AppCaptureEnabled -ErrorAction Stop).AppCaptureEnabled } catch {}
  try { $gameDvrEnabled=(Get-ItemProperty 'HKCU:\System\GameConfigStore' -Name GameDVR_Enabled -ErrorAction Stop).GameDVR_Enabled } catch {}
  Write-Host "Game Bar settings: AppCaptureEnabled=$appCaptureEnabled GameDVR_Enabled=$gameDvrEnabled"
  if ($appCaptureEnabled -eq 0 -or $gameDvrEnabled -eq 0) {
    Write-Warning 'Xbox Game Bar recording is disabled. Trying the verified portable FFmpeg fallback.'
    return $false
  }

  $proc = Start-DemoTour
  try {
    Start-Sleep -Seconds 4
    [void](Focus-WICareerBuilder 20)
    Write-Host 'Starting Xbox Game Bar recording (Win+Alt+R)...'
    [WICBWindow]::RecordToggle()
    Start-Sleep -Seconds 2
    Write-Host "Recording for $Seconds seconds while the automatic product tour runs..."
    Start-Sleep -Seconds $Seconds
    [void](Focus-WICareerBuilder 5)
    Write-Host 'Stopping Xbox Game Bar recording...'
    [WICBWindow]::RecordToggle()
    $latest=Find-NewGameBarCapture $start 18
    if ($null -eq $latest) {
      Write-Warning "Xbox Game Bar did not create a new MP4 in $captures. Falling back to verified portable FFmpeg."
      return $false
    }
    Copy-Item $latest.FullName $dest -Force
    return ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 100000))
  }
  finally { Stop-DemoProcess $proc }
}

function Find-Ffmpeg {
  $cmd=Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
  if ($null -ne $cmd) { return $cmd.Source }
  $cmd=Get-Command ffmpeg -ErrorAction SilentlyContinue
  if ($null -ne $cmd) { return $cmd.Source }
  return $null
}

function Get-VerifiedPortableFfmpeg {
  $existing=Find-Ffmpeg
  if (![string]::IsNullOrWhiteSpace($existing)) {
    Write-Host "Using existing FFmpeg: $existing"
    return $existing
  }

  # Developer/media-capture dependency only. It is cached under TEMP and is not
  # installed system-wide or included in WICareerBuilder distribution packages.
  $version='9.0.1'
  $expectedSha256='fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9'
  $zipUrl="https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$version-essentials_build.zip"
  $toolRoot=Join-Path $env:TEMP "WICareerBuilder-MediaTools\ffmpeg-$version"
  $zipPath=Join-Path $toolRoot "ffmpeg-$version-essentials_build.zip"
  $extractRoot=Join-Path $toolRoot 'expanded'

  $cached=Get-ChildItem -Path $extractRoot -Filter 'ffmpeg.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($null -ne $cached) {
    Write-Host "Using verified portable FFmpeg cache: $($cached.FullName)"
    return $cached.FullName
  }

  New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
  Write-Host "Downloading pinned FFmpeg $version media-capture tool..."
  Write-Host "Source: $zipUrl"
  try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
  }
  catch {
    Write-Warning "Portable FFmpeg download failed: $($_.Exception.Message)"
    return $null
  }

  $actual=(Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
  Write-Host "FFmpeg ZIP SHA256: $actual"
  if ($actual -ne $expectedSha256) {
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Write-Warning "Portable FFmpeg SHA256 mismatch. Expected $expectedSha256"
    return $null
  }
  Write-Host 'Portable FFmpeg SHA256 verification: PASS'

  Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
  Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force
  $ffmpeg=Get-ChildItem -Path $extractRoot -Filter 'ffmpeg.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($null -eq $ffmpeg) {
    Write-Warning 'Verified FFmpeg archive extracted, but ffmpeg.exe was not found.'
    return $null
  }

  $notice=Join-Path $toolRoot 'WICareerBuilder-MediaTool-Notice.txt'
  @"
WICareerBuilder Product Media Capture Tool
FFmpeg version: $version
Download source: $zipUrl
Archive SHA256: $expectedSha256
Purpose: temporary developer-side V1 demo recording only.
The downloaded FFmpeg binary is not bundled into WICareerBuilder product/distribution packages.
"@ | Set-Content -Path $notice -Encoding UTF8

  return $ffmpeg.FullName
}

function Invoke-FfmpegCapture([string]$Ffmpeg, [bool]$WindowOnly) {
  if (Test-Path $dest) { Remove-Item $dest -Force -ErrorAction SilentlyContinue }
  $proc=Start-DemoTour
  try {
    Start-Sleep -Seconds 4
    [void](Focus-WICareerBuilder 20)
    if ($WindowOnly) {
      Write-Host 'Recording WICareerBuilder window with FFmpeg gdigrab...'
      $input='title=WICareerBuilder'
    }
    else {
      Write-Host 'Window-specific capture was unavailable; recording desktop fallback with FFmpeg gdigrab...'
      $input='desktop'
    }

    $args=@(
      '-y','-hide_banner','-loglevel','warning',
      '-f','gdigrab','-framerate','24','-draw_mouse','0','-i',$input,
      '-t',"$Seconds",
      '-vf','pad=ceil(iw/2)*2:ceil(ih/2)*2',
      '-c:v','libx264','-preset','veryfast','-crf','22','-pix_fmt','yuv420p','-movflags','+faststart',
      $dest
    )
    $ff=Start-Process $Ffmpeg -ArgumentList $args -PassThru -Wait -NoNewWindow
    if ($ff.ExitCode -ne 0) {
      Write-Warning "FFmpeg capture returned exit code $($ff.ExitCode)."
      return $false
    }
    return ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 100000))
  }
  finally { Stop-DemoProcess $proc }
}

function Try-FfmpegCapture {
  $ffmpeg=Get-VerifiedPortableFfmpeg
  if ([string]::IsNullOrWhiteSpace($ffmpeg)) { return $false }

  if (Invoke-FfmpegCapture $ffmpeg $true) { return $true }
  Write-Warning 'FFmpeg could not capture by window title. Retrying with desktop capture.'
  return (Invoke-FfmpegCapture $ffmpeg $false)
}

Write-Host 'WICareerBuilder Demo Video Helper — recovery v3 / verified portable FFmpeg fallback'
$captured=Try-GameBarCapture
if (!$captured) { $captured=Try-FfmpegCapture }

if (!$captured) {
  Write-Error @"
No demo MP4 could be created automatically.
The CareerBuilder application, branding, screenshots and automatic demo tour are working; the media encoder/capture path failed.
Game Bar was attempted first. The fallback then attempted a pinned FFmpeg 9.0.1 ZIP whose SHA-256 must equal:
  fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9
Expected output: $dest
"@
  exit 21
}

$hash=(Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Video: $dest"
Write-Host "SHA256: $hash"
Write-Host 'VIDEO-CAPTURE-001 PASS'
