# MyZoom 제거: 프로그램·바로가기·자동 실행 등록을 지운다. 설정(%APPDATA%\MyZoom)은 -RemoveSettings 일 때만 지운다.
param([switch]$RemoveSettings)
$dest = Join-Path $env:LOCALAPPDATA 'Programs\MyZoom'
Get-Process MyZoom -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'MyZoom.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'MyZoom.lnk') -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MyZoom' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MyZoom' -Recurse -ErrorAction SilentlyContinue
if ($RemoveSettings) { Remove-Item (Join-Path $env:APPDATA 'MyZoom') -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host 'MyZoom 을 제거했습니다.' -ForegroundColor Green
