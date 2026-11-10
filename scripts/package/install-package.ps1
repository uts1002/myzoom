# MyZoom 설치 (묶음용, 빌드 없음): 이 폴더의 MyZoom.exe 를 %LOCALAPPDATA%\Programs\MyZoom 에 복사하고 바로가기·자동 실행 등록
param([switch]$NoDesktopShortcut, [switch]$NoStartup, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'MyZoom.exe'
if (-not (Test-Path $src)) { throw "MyZoom.exe 가 없습니다: $src (zip 을 먼저 풀어 주세요)" }
$dest = Join-Path $env:LOCALAPPDATA 'Programs\MyZoom'

Write-Host '[1/3] 복사 중...' -ForegroundColor Cyan
Get-Process MyZoom -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $src $dest -Force
$exe = Join-Path $dest 'MyZoom.exe'

Write-Host '[2/3] 바로가기 만드는 중...' -ForegroundColor Cyan
$shell = New-Object -ComObject WScript.Shell
function New-Shortcut($path) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'MyZoom - 화면 확대·판서'
    $lnk.Save()
}
New-Shortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'MyZoom.lnk')
if (-not $NoDesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'MyZoom.lnk') }
if (-not $NoStartup) {
    Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MyZoom' -Value "`"$exe`""
}

Write-Host '[3/3] 실행' -ForegroundColor Cyan
if (-not $NoLaunch) { Start-Process $exe }
Write-Host ''
Write-Host "설치 완료: $exe" -ForegroundColor Green
Write-Host '작업 표시줄 오른쪽 아래 트레이에 돋보기 아이콘이 생깁니다. Ctrl+1 확대, Ctrl+4 라이브 줌.'
