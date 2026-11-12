# 새 버전 내보내기: 태그를 만들어 올리면 GitHub Actions 가 빌드해서 릴리즈를 만들고,
# 설치된 MyZoom 들은 6시간 안에(또는 다음에 켤 때 30초 뒤) 알아서 새 버전으로 바뀐다.
#   .\release.ps1 1.2.0 "타이머 제목 바꾸기 추가"
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Notes
)
# git 은 진행 메시지를 오류 출력으로 내보내므로 Stop 을 쓰지 않고 종료 코드로 확인한다 (PowerShell 5.1)
$ErrorActionPreference = 'Continue'
function Git-Run { & git @args 2>&1 | ForEach-Object { "$_" }; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') 실패" } }

[void][version]$Version
if (git status --porcelain --untracked-files=no) { throw '커밋하지 않은 변경이 있어요. 먼저 커밋하세요.' }
Git-Run tag -a "v$Version" -m $Notes
Git-Run push origin HEAD
Git-Run push origin "v$Version"
Write-Host "v$Version 태그를 올렸어요. 릴리즈는 3~5분 뒤 https://github.com/uts1002/myzoom/releases 에 생겨요." -ForegroundColor Green
