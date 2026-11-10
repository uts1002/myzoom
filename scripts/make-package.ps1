# 다른 PC용 설치 묶음 만들기: .NET 없이 도는 단일 exe + 설치/제거 bat → dist\MyZoom설치.zip
# 받는 PC 에는 .NET SDK·런타임이 없어도 된다. zip 풀고 「설치.bat」 더블클릭.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pkg = Join-Path $root 'dist\MyZoom설치'
$zip = Join-Path $root 'dist\MyZoom설치.zip'

Write-Host '[1/3] 빌드 중 (런타임 포함)...' -ForegroundColor Cyan
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
& dotnet publish (Join-Path $root 'MyZoom.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $pkg --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

Write-Host '[2/3] 설치 파일 넣는 중...' -ForegroundColor Cyan
Copy-Item (Join-Path $PSScriptRoot 'package\*') $pkg -Force

Write-Host '[3/3] 압축 중...' -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item $zip -Force }
# Compress-Archive(PS 5.1)는 경로를 \ 로 적어서 ZipFile 로 만든다 (한글 이름은 UTF-8)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pkg, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "완료: $zip ($mb MB)" -ForegroundColor Green
