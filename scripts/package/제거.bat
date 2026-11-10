@echo off
rem MyZoom uninstall
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall-package.ps1" %*
echo.
pause
