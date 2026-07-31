<#
.SYNOPSIS
  WinDimmer를 self-contained로 publish하고 서명 없는 MSIX 패키지로 묶는다.

  Store 제출용은 서명이 필요 없다 — 업로드하면 Microsoft가 재서명하므로 SmartScreen
  경고 없이 배포된다. Identity 3개 파라미터는 Partner Center > 제품 관리 > 제품 ID
  페이지의 값을 그대로 넘긴다. 파라미터 없이 실행하면 로컬 테스트용 기본값을 쓴다.

.EXAMPLE
  .\build-msix.ps1 -IdentityName "12345MyName.WinDimmer" `
                   -Publisher "CN=A1B2C3D4-....." `
                   -PublisherDisplayName "홍길동"
#>
param(
    [string]$IdentityName = "WinDimmer.Dev",
    [string]$Publisher = "CN=WinDimmer.Dev",
    [string]$PublisherDisplayName = "WinDimmer Dev",
    [string]$DisplayName = "WinDimmer",
    # 생략하면 csproj의 <Version>을 쓴다. Store 규칙상 4번째 자리는 0이어야 한다.
    [string]$Version,
    [string]$OutDir = (Join-Path $PSScriptRoot "out")
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $repoRoot "src\WinDimmer\WinDimmer.csproj"

# ── 버전: csproj <Version> → 4자리 (1.0.0 → 1.0.0.0)
if (-not $Version) {
    $m = [regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>')
    if (-not $m.Success) { throw "csproj에서 <Version>을 찾지 못했습니다." }
    $Version = $m.Groups[1].Value
}
$parsed = $null
if (-not [Version]::TryParse($Version, [ref]$parsed)) {
    throw "버전 형식이 잘못됐습니다: '$Version' — 1.0.0 같은 숫자 형식이어야 합니다 (프리릴리스 접미사 불가)."
}
# Store 규칙: 4번째 자리는 항상 0
$Version = "{0}.{1}.{2}.0" -f $parsed.Major, $parsed.Minor, [Math]::Max($parsed.Build, 0)

# ── 1) self-contained publish — 받는 쪽에 .NET 런타임이 없어도 실행되게 한다
# publish는 출력 폴더의 이전 파일을 지우지 않으므로, 이름이 바뀐 어셈블리가 남아
# 패키지에 섞여 들어가지 않게 매번 비운다.
$publishDir = Join-Path $OutDir "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패" }

# ── 2) 스테이징: publish 출력 + 로고 + 토큰 채운 매니페스트
$staging = Join-Path $OutDir "staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item (Join-Path $publishDir "*") $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot "Assets") (Join-Path $staging "Assets") -Recurse

$manifest = (Get-Content (Join-Path $PSScriptRoot "Package.appxmanifest") -Raw).
    Replace('__IDENTITY_NAME__', $IdentityName).
    Replace('__PUBLISHER__', $Publisher).
    Replace('__PUBLISHER_DISPLAY_NAME__', $PublisherDisplayName).
    Replace('__DISPLAY_NAME__', $DisplayName).
    Replace('__VERSION__', $Version)
Set-Content (Join-Path $staging "AppxManifest.xml") $manifest -Encoding utf8

# ── 3) makeappx: Windows SDK가 있으면 그걸, 없으면 NuGet SDK BuildTools를 복원해 쓴다
function Find-MakeAppx {
    $fromKits = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($fromKits) { return $fromKits.FullName }

    $glob = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe"
    $fromNuget = Get-ChildItem $glob -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($fromNuget) { return $fromNuget.FullName }

    Write-Host "makeappx가 없어 Microsoft.Windows.SDK.BuildTools NuGet 패키지를 복원합니다..."
    # 함수 반환값에 restore 출력이 섞이지 않도록 호스트로 바로 보낸다
    dotnet restore (Join-Path $PSScriptRoot "tools\SdkTools.csproj") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "SDK BuildTools 복원 실패" }
    $fromNuget = Get-ChildItem $glob -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($fromNuget) { return $fromNuget.FullName }
    throw "makeappx.exe를 찾지 못했습니다."
}
$makeappx = Find-MakeAppx

# ── 4) 패키징 (makeappx가 매니페스트 검증도 같이 한다)
$msix = Join-Path $OutDir "WinDimmer_${Version}_x64.msix"
& $makeappx pack /o /d $staging /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx pack 실패" }

Write-Host ""
Write-Host "완료: $msix"
Write-Host "Store 제출: 서명 없이 이 파일을 그대로 업로드한다 (Store가 서명한다)."
Write-Host "로컬 테스트: 개발자 모드를 켠 뒤  Add-AppxPackage -Register `"$staging\AppxManifest.xml`""
