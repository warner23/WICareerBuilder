@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-WICareerBuilder.ps1" -SourceDirectory "%~dp0"
if errorlevel 1 pause
endlocal
