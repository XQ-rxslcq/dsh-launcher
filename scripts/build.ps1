# build.ps1 (project-specific script)
# 编译 dsh-launcher.exe（WPF，.NET Framework 4.x，无需 SDK），然后组装：
#   dist/dsh-launcher.exe            -> 独立启动器 + config.json
#   plugin/dist/dsh-launcher.exe     -> 插件 bundle（纯代码，不嵌入任何图片素材）
#
# 用法：
#   pwsh scripts/build.ps1                     # 纯代码编译（exe 用系统默认图标）
#   pwsh scripts/build.ps1 -Icon icon.png      # 指定图标（png/ico/jpg/bmp，自动转 ico 嵌入）
#
# 说明：
#   - 「exe 文件图标」是编译期嵌入 PE 的，本脚本通过 /win32icon 实现；
#   - 不指定 -Icon 且本地无 assets/icon.ico 时，纯代码编译，不携带任何图片；
#   - 加载动画表情与启动器窗口图标仍由用户在设置页通过 stickerDir/iconPath 绑定。
param(
  [string]$Icon = ""   # 可选：图标文件路径（png/ico/jpg/bmp），编译时嵌入为 exe 文件图标
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent $PSScriptRoot   # projects/dsh-launcher
$dist = Join-Path $proj 'dist'
$pluginDir = Join-Path $proj 'plugin'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $pluginDir 'dist') -Force | Out-Null

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
    $hicon = $bmp.GetHicon()
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
  $local = Join-Path $proj 'assets\icon.ico'
  if (Test-Path $local) { $iconIco = $local }
}

$iconArgs = @()
if ($iconIco) { $iconArgs += "/win32icon:$iconIco" }

# --- compile ---
$outExe = Join-Path $dist 'dsh-launcher.exe'
$args = @(
  '/nologo', '/target:winexe',
  "/out:$outExe"
) + $iconArgs + $refs + @(Join-Path $proj 'src\Launcher.cs')

& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

# --- standalone config ---
$cfg = Join-Path $proj 'config.json'
if (Test-Path $cfg) { Copy-Item $cfg (Join-Path $dist 'config.json') -Force }

# --- assemble plugin bundle（纯代码，仅 exe） ---
Copy-Item $outExe (Join-Path $pluginDir 'dist\dsh-launcher.exe') -Force

Write-Output "Built: $outExe ($((Get-Item $outExe).Length) bytes)"
if ($iconIco) { Write-Output "exe 图标已嵌入: $iconIco" } else { Write-Output "纯代码编译（exe 用系统默认图标）" }
Write-Output "Plugin assembled: $pluginDir"
