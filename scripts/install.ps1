# MyZoom 설치: 빌드(Release) → %LOCALAPPDATA%\Programs\MyZoom 에 복사 → 시작 메뉴·바탕화면 바로가기 → 실행
# 관리자 권한 필요 없음. 다시 실행하면 업데이트(덮어쓰기)된다.
param([switch]$NoDesktopShortcut, [switch]$NoStartup, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\MyZoom'
$publish = Join-Path $root 'publish'

Write-Host '[1/4] 빌드 중...' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 가 없습니다. https://dotnet.microsoft.com/download 에서 .NET 9 SDK 를 설치하세요.'
}
& dotnet publish (Join-Path $root 'MyZoom.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

Write-Host '[2/4] 복사 중...' -ForegroundColor Cyan
Get-Process MyZoom -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $publish '*') $dest -Recurse -Force
$exe = Join-Path $dest 'MyZoom.exe'

Write-Host '[3/4] 바로가기 만드는 중...' -ForegroundColor Cyan
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

Write-Host '[4/4] 실행' -ForegroundColor Cyan
if (-not $NoLaunch) { Start-Process $exe }
Write-Host ''
Write-Host "설치 완료: $exe" -ForegroundColor Green
Write-Host '작업 표시줄 오른쪽 아래 트레이에 돋보기 아이콘이 생깁니다. Ctrl+1 확대, Ctrl+2 그리기.'
