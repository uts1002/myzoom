@echo off
rem MyZoom installer: downloads the latest MyZoom.exe from GitHub Releases and installs it for this user (no admin).
echo MyZoom - downloading the latest version (about 60 MB)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072; $f = Join-Path $env:TEMP 'MyZoom-Setup.exe'; Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/uts1002/myzoom/releases/latest/download/MyZoom.exe' -OutFile $f; (Start-Process -FilePath $f -ArgumentList '--install' -PassThru).WaitForExit()"
if errorlevel 1 (
  echo.
  echo Download failed. Check the internet connection and try again.
  pause
  exit /b 1
)
echo Done. MyZoom is in the tray (bottom right).
timeout /t 3 > nul
