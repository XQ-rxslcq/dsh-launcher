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
    # 缩放到 256x256（保留 alpha 通道）
    $size = 256
    $scaled = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    try {
      $g.Clear([System.Drawing.Color]::Transparent)
      $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.DrawImage($bmp, 0, 0, $size, $size)
    } finally { $g.Dispose() }
    # 保存为 PNG 并直接嵌入 ICO（PNG 格式 ico，保留原始颜色与透明度，避免 GetHicon 的颜色降级）
    $ms = New-Object System.IO.MemoryStream
    try {
      $scaled.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
      $pngBytes = $ms.ToArray()
    } finally { $ms.Dispose() }
    $icoPath = [System.IO.Path]::ChangeExtension($imgPath, '.ico')
    $fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
      $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)   # ICONDIR
      $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)  # 256x256, 0=256
      $bw.Write([UInt16]1); $bw.Write([UInt16]32)                        # planes, bitCount
      $bw.Write([UInt32]$pngBytes.Length); $bw.Write([UInt32]22)         # bytesInRes, imageOffset
      $bw.Write($pngBytes)                                               # PNG 数据
    } finally { $bw.Dispose(); $fs.Dispose() }
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

# --- compile（先编译到临时文件，成功后原子替换，避免覆盖运行中的 exe 失败） ---
$outExe = Join-Path $dist 'dsh-launcher.exe'
$tmpExe = "$outExe.tmp"
if (Test-Path $tmpExe) { Remove-Item $tmpExe -Force }
$args = @(
  '/nologo', '/target:winexe',
  "/out:$tmpExe"
) + $iconArgs + $refs + @($src)

& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

# 原子替换：先删旧 exe，再把临时文件移动为正式名（若 exe 正被占用会在此失败并提示）
if (Test-Path $outExe) { Remove-Item $outExe -Force -ErrorAction SilentlyContinue }
Move-Item $tmpExe $outExe -Force

# 通知系统刷新图标（Shell API），尽量让资源管理器立即显示新图标
try {
  if (-not ('IconRefresh' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class IconRefresh {
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(int wEventId, int uFlags, string dwItem1, IntPtr dwItem2);
}
"@
  }
  # SHCNE_UPDATEITEM(0x2000) | SHCNF_PATHW(0x5)：通知该文件图标更新
  [IconRefresh]::SHChangeNotify(0x2000, 0x5, $outExe, [IntPtr]::Zero)
  # SHCNE_ASSOCCHANGED(0x8000000) | SHCNF_IDLIST(0x0)：通知全局图标/关联变更，强制刷新缓存
  [IconRefresh]::SHChangeNotify(0x8000000, 0x0, [IntPtr]::Zero, [IntPtr]::Zero)
} catch { }

# 再触发一次系统图标缓存刷新
try { & "$env:WINDIR\System32\ie4uinit.exe" -show 2>$null | Out-Null } catch { }

# --- standalone config（独立 exe 的默认配置，不进 npm 包） ---
$cfg = Join-Path $root 'config.json'
if (Test-Path $cfg) { Copy-Item $cfg (Join-Path $dist 'config.json') -Force }

Write-Output "Built: $outExe ($((Get-Item $outExe).Length) bytes)"
if ($iconIco) { Write-Output "exe 图标已嵌入: $iconIco" } else { Write-Output "纯代码编译（exe 用系统默认图标）" }
