# build.ps1 (project-specific script)
# 编译 dsh-launcher.exe（WPF，.NET Framework 4.x，无需 SDK），然后组装：
#   dist/dsh-launcher.exe            -> 独立启动器 + config.json（源码安装）
#   plugin/dist/dsh-launcher.exe     -> 插件 bundle（纯代码，不嵌入任何图片素材）
#
# 用法：
#   pwsh scripts/build.ps1                     # 纯代码编译（exe 用系统默认图标）
#   pwsh scripts/build.ps1 -Icon icon.png      # 指定图标（png/ico/jpg/bmp，自动转 ico 嵌入）
#
# 说明：
#   - 本脚本在「源码安装」和「npm/tgz 包内」两种场景都能运行：
#       源码安装：$root 是项目根，含 plugin/ 子目录，会同步 exe 与编译源码到 plugin/
#       npm 安装：$root 是包根（即插件目录），直接编译覆盖包内 dist/ 下的 exe
#   - 「exe 文件图标」是编译期嵌入 PE 的，通过 /win32icon 实现；
#   - 不指定 -Icon 且无 assets/icon.ico 时，纯代码编译，不携带任何图片；
#   - 加载动画表情与启动器窗口图标仍由用户在设置页通过 stickerDir/iconPath 绑定。
param(
  [string]$Icon = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # 项目根（源码安装）或 包根（npm 安装）
$dist = Join-Path $root 'dist'
$src = Join-Path $root 'src\Launcher.cs'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

# --- locate csc.exe (prefer 64-bit Framework) ---
$csc = $null
foreach ($base in @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319")) {
  $cand = Join-Path $base 'csc.exe'
  if (Test-Path $cand) { $csc = $cand; break }
}
if (-not $csc) { throw 'csc.exe (.NET Framework 4.x) not found' }

# --- references (WPF assemblies) ---
$refBase = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path (Join-Path $refBase 'PresentationFramework.dll'))) { throw "Reference assemblies v4.8 not found at $refBase" }
$refs = @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','System.Xaml.dll') |
  ForEach-Object { "/reference:" + (Join-Path $refBase $_) }

# --- 解析图标：指定 -Icon 或本地 assets/icon.ico ---
function Convert-ToIco([string]$imgPath) {
  Add-Type -AssemblyName System.Drawing
  $bmp = New-Object System.Drawing.Bitmap($imgPath)
  try {
    # 缩放到 256x256（标准 exe 图标尺寸；原图过大时 csc 的 /win32icon 会拒绝）
    $size = 256
    $scaled = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    try {
      $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.DrawImage($bmp, 0, 0, $size, $size)
    } finally { $g.Dispose() }
    $hicon = $scaled.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hicon)
    $icoPath = [System.IO.Path]::ChangeExtension($imgPath, '.ico')
    $fs = [System.IO.File]::Create($icoPath)
    try { $icon.Save($fs) } finally { $fs.Dispose() }
    return $icoPath
  } finally { $bmp.Dispose() }
}

$iconIco = $null
if ($Icon -and (Test-Path $Icon)) {
  if ($Icon -match '\.ico$') { $iconIco = $Icon }
  else { $iconIco = Convert-ToIco $Icon; Write-Output "图标已转换: $iconIco" }
} else {
  $local = Join-Path $root 'assets\icon.ico'
  if (Test-Path $local) { $iconIco = $local }
}

$iconArgs = @()
if ($iconIco) { $iconArgs += "/win32icon:$iconIco" }

# --- compile ---
$outExe = Join-Path $dist 'dsh-launcher.exe'
$args = @(
  '/nologo', '/target:winexe',
  "/out:$outExe"
) + $iconArgs + $refs + @($src)

& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

# --- standalone config（独立 exe 的默认配置，不进 npm 包） ---
$cfg = Join-Path $root 'config.json'
if (Test-Path $cfg) { Copy-Item $cfg (Join-Path $dist 'config.json') -Force }

Write-Output "Built: $outExe ($((Get-Item $outExe).Length) bytes)"
if ($iconIco) { Write-Output "exe 图标已嵌入: $iconIco" } else { Write-Output "纯代码编译（exe 用系统默认图标）" }
