@echo off
setlocal
cd /d "%~dp0"

where pyw.exe >nul 2>nul
if %errorlevel%==0 (
  start "" /b pyw.exe -3 "%~dp0app.py"
  exit /b 0
)

where pythonw.exe >nul 2>nul
if %errorlevel%==0 (
  start "" /b pythonw.exe "%~dp0app.py"
  exit /b 0
)

echo No se encontro Python 3 con soporte para aplicaciones de escritorio.
pause
exit /b 1
