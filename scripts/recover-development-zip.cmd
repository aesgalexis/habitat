@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0recover-development-zip.ps1" -PackagePath "%~dp0HABITAT-0.16.4-win-x64.zip" %*
if errorlevel 1 (
  echo HABITAT recovery failed. The error is shown above.
  pause
  exit /b 1
)
echo HABITAT recovery completed.
pause
