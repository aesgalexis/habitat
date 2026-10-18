@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-HABITAT.ps1" %*
if errorlevel 1 (
  echo HABITAT installation failed. See the error above.
  pause
  exit /b 1
)
