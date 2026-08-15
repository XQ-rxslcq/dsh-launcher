# build.ps1 (project-specific script)
# Compile dsh-launcher.exe (WPF, .NET Framework 4.x, no SDK required), then assemble:
#   dist/dsh-launcher.exe            -> standalone launcher + config.json
#   plugin/dist/dsh-launcher.exe     -> plugin bundle (纯代码，不含任何图片素材)
# Usage: pwsh scripts/build.ps1
#
# 说明：本脚本【不嵌入任何图片资源】——桌面图标与加载动画表情由用户在设置页
# 通过 iconPath / stickerDir 自行绑定（指向本地图片/目录），因此仓库可纯代码分发，
# 不携带任何第三方角色素材。
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

# --- references (WPF assemblies live in Reference Assemblies) ---
$refBase = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path (Join-Path $refBase 'PresentationFramework.dll'))) { throw "Reference assemblies v4.8 not found at $refBase" }
$refs = @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','System.Xaml.dll') |
  ForEach-Object { "/reference:" + (Join-Path $refBase $_) }

# --- compile ---
# 可选：若本地存在 assets/icon.ico（已 .gitignore，不进仓库），编译时嵌入为 exe 文件图标；
# 否则纯代码编译，exe 使用系统默认图标。
$outExe = Join-Path $dist 'dsh-launcher.exe'
$iconIco = Join-Path $proj 'assets\icon.ico'
$iconArgs = @()
if (Test-Path $iconIco) { $iconArgs += "/win32icon:$iconIco" }
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
Write-Output "Plugin assembled: $pluginDir"
