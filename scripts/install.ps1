# (개발용) 이 PC 에서 소스를 빌드해 설치: 빌드 → MyZoom.exe --install (복사·바로가기·자동 실행·'설치된 앱' 등록 후 실행)
# 직접 빌드한 개발판(버전 0.0.0)은 자동 업데이트를 하지 않는다. 일반 설치는 GitHub 릴리즈의 MyZoom-Setup.cmd 를 쓴다.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'

Write-Host '[1/2] 빌드 중...' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 가 없습니다. 일반 설치는 https://github.com/uts1002/myzoom/releases 의 MyZoom-Setup.cmd 를 쓰세요.'
}
& dotnet publish (Join-Path $root 'MyZoom.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

Write-Host '[2/2] 설치 중...' -ForegroundColor Cyan
# -Wait 은 설치 후 켜지는 MyZoom 까지 기다리므로, 설치 프로세스만 기다린다
(Start-Process (Join-Path $publish 'MyZoom.exe') -ArgumentList '--install' -PassThru).WaitForExit()
Write-Host ''
Write-Host "설치 완료: $env:LOCALAPPDATA\Programs\MyZoom\MyZoom.exe" -ForegroundColor Green
