<#
  src/WinDimmer/app.ico의 가장 큰 프레임(256px)에서 MSIX 매니페스트가 요구하는
  로고 PNG 3종을 만들어 packaging/Assets에 넣는다. 아이콘이 바뀌면 다시 실행한다.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$icoPath = Join-Path (Split-Path $PSScriptRoot -Parent) "src\WinDimmer\app.ico"
$outDir = Join-Path $PSScriptRoot "Assets"
New-Item -ItemType Directory -Force $outDir | Out-Null

# ICO 디렉터리를 직접 파싱해 가장 큰 프레임을 찾는다. 256px 프레임은 보통 PNG로 압축돼
# 있어 System.Drawing.Icon으로는 온전히 못 꺼내는 경우가 있다.
$bytes = [IO.File]::ReadAllBytes($icoPath)
$count = [BitConverter]::ToUInt16($bytes, 4)
$best = $null
for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + 16 * $i
    $w = [int]$bytes[$o]; if ($w -eq 0) { $w = 256 }
    $entry = @{
        W    = $w
        Size = [BitConverter]::ToUInt32($bytes, $o + 8)
        Off  = [BitConverter]::ToUInt32($bytes, $o + 12)
    }
    if ($null -eq $best -or $entry.W -gt $best.W) { $best = $entry }
}

$frame = New-Object byte[] $best.Size
[Array]::Copy($bytes, $best.Off, $frame, 0, $best.Size)

if ($frame[0] -eq 0x89 -and $frame[1] -eq 0x50) {
    # PNG 압축 프레임
    $src = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream(, $frame)))
}
else {
    $ico = New-Object System.Drawing.Icon((New-Object IO.MemoryStream(, $bytes)), $best.W, $best.W)
    $src = $ico.ToBitmap()
}

function Save-Png([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $path = Join-Path $outDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "생성: $path"
}

Save-Png 150 "Square150x150Logo.png"
Save-Png 44  "Square44x44Logo.png"
Save-Png 50  "StoreLogo.png"
$src.Dispose()
