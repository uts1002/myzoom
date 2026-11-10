@echo off
rem MyZoom install (double-click, no .NET needed)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-package.ps1" %*
echo.
pause
