# build-installer.ps1 — 构建醒酒器 0.11 安装包（可重复执行）
# 产物：
#   run\installer\Decanter-Setup-0.11.exe   单文件安装器（内嵌 payload.zip）
#   run\installer\Uninstall.exe            卸载器（构建中间产物，也被打进 payload.zip）
# 用法：pwsh -File setup\build-installer.ps1   （或 Windows PowerShell 5.1 直接运行）
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$setupDir = $PSScriptRoot
$rootDir = Split-Path -Parent $setupDir
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# 源码树可能是两种布局：
#   A) 工作区：<root>\work\App、<root>\run
#   B) 交付源码：<root>\App、<root>\..\run（build.ps1 输出到 source 的上一级 run）
$appRoot = Join-Path $rootDir 'run'
if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'Decanter.exe'))) {
    $altRun = Join-Path (Split-Path -Parent $rootDir) 'run'
    if (Test-Path -LiteralPath (Join-Path $altRun 'Decanter.exe')) { $appRoot = $altRun }
}
$icon = Join-Path $rootDir 'work\App\app.ico'
if (-not (Test-Path -LiteralPath $icon)) { $icon = Join-Path $rootDir 'App\app.ico' }
if (-not (Test-Path -LiteralPath $icon)) { $icon = Join-Path $rootDir 'source\App\app.ico' }
$manifest = Join-Path $setupDir 'asInvoker.manifest'
$buildDir = Join-Path $setupDir 'build'
$payloadDir = Join-Path $buildDir 'payload'
$zipPath = Join-Path $buildDir 'payload.zip'
$outDir = Join-Path $appRoot 'installer'
$outSetup = Join-Path $outDir 'Decanter-Setup-0.11.exe'
$uninstallExe = Join-Path $buildDir 'Uninstall.exe'
$setupSrc = Join-Path $setupDir 'Setup.cs'
$uninstallSrc = Join-Path $setupDir 'Uninstall.cs'
$uiSrc = Join-Path $setupDir 'SetupUi.cs'
$readmeSrc = Join-Path $setupDir 'payload-README.txt'

if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc.exe：$csc" }
if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'Decanter.exe'))) { throw "找不到应用产物：$(Join-Path $appRoot 'Decanter.exe')；请先运行 build.ps1" }
foreach ($p in @($manifest, $setupSrc, $uninstallSrc, $uiSrc, $readmeSrc)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "缺少源文件：$p" }
}
if (-not (Test-Path -LiteralPath $icon)) { throw "找不到图标：$icon" }

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 程序集引用：csc 会从 GAC 解析简单名
$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll'
)

$common = @('/nologo', '/platform:x64', '/optimize+', ('/win32manifest:' + $manifest), ('/win32icon:' + $icon))

# ---------- 1) 先编译卸载器 ----------
Write-Host '[1/5] 编译 Uninstall.exe ...'
$uninstallArgs = @('/target:winexe') + $common + @(('/out:' + $uninstallExe)) + $refs + @($uiSrc, $uninstallSrc)
& $csc @uninstallArgs
if ($LASTEXITCODE -ne 0) { throw 'Uninstall.exe 编译失败' }
if (-not (Test-Path -LiteralPath $uninstallExe)) { throw 'Uninstall.exe 没有生成' }
Copy-Item -LiteralPath $uninstallExe -Destination (Join-Path $outDir 'Uninstall.exe') -Force

# ---------- 2) 准备 payload 目录 ----------
Write-Host '[2/5] 准备 payload ...'
$payloadFiles = @(
    @{ Src = (Join-Path $appRoot 'Decanter.exe'); Name = 'Decanter.exe' },
    @{ Src = (Join-Path $appRoot 'Decanter.exe.config'); Name = 'Decanter.exe.config' },
    @{ Src = (Join-Path $appRoot 'sqlite3.dll'); Name = 'sqlite3.dll' },
    @{ Src = $uninstallExe; Name = 'Uninstall.exe' },
    @{ Src = $readmeSrc; Name = 'README.txt' }
)
$zipInputs = @()
foreach ($item in $payloadFiles) {
    if (-not (Test-Path -LiteralPath $item.Src)) { throw "缺少 payload 文件：$($item.Src)" }
    $dst = Join-Path $payloadDir $item.Name
    Copy-Item -LiteralPath $item.Src -Destination $dst -Force
    $zipInputs += $dst
}

# ---------- 3) 压缩 payload.zip（只装这 5 个文件） ----------
Write-Host '[3/5] 生成 payload.zip ...'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path $zipInputs -DestinationPath $zipPath -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zipPath)) { throw 'payload.zip 没有生成' }

# ---------- 4) 编译单文件安装器（payload.zip 作为嵌入资源） ----------
Write-Host '[4/5] 编译 Decanter-Setup-0.11.exe ...'
$setupArgs = @('/target:winexe') + $common + @(('/out:' + $outSetup)) +
    @(('/resource:' + $zipPath + ',payload.zip')) + $refs + @($uiSrc, $setupSrc)
& $csc @setupArgs
if ($LASTEXITCODE -ne 0) { throw 'Decanter-Setup-0.11.exe 编译失败' }
if (-not (Test-Path -LiteralPath $outSetup)) { throw 'Decanter-Setup-0.11.exe 没有生成' }

# ---------- 5) 汇总 ----------
Write-Host '[5/5] 完成'
$setupSize = [math]::Round((Get-Item -LiteralPath $outSetup).Length / 1MB, 2)
$unSize = [math]::Round((Get-Item -LiteralPath (Join-Path $outDir 'Uninstall.exe')).Length / 1MB, 2)
$zipSize = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)
Write-Host ("  安装器 : {0}  ({1} MB)" -f $outSetup, $setupSize)
Write-Host ("  卸载器 : {0}  ({1} MB)" -f (Join-Path $outDir 'Uninstall.exe'), $unSize)
Write-Host ("  payload: {0}  ({1} MB)" -f $zipPath, $zipSize)
Write-Host '下一步（可选）：截图与沙箱自测，见 setup\INSTALLER-NOTES.md'
