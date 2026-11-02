# build.ps1 - compile Decanter with the built-in .NET Framework 4.8 C# compiler (C# 5)
# ASCII only (PowerShell 5.1 + Chinese = parse errors)
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $src 'run'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$outDir = (Resolve-Path $outDir).Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$core = Get-ChildItem (Join-Path $src 'Core') -Filter '*.cs' | ForEach-Object { $_.FullName }
$app = Get-ChildItem (Join-Path $src 'App') -Filter '*.cs' | ForEach-Object { $_.FullName }
$refs = @(
  '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
  '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.Data.dll'
)
$manifest = Join-Path $src 'App\app.manifest'
$icon = Join-Path $src 'App\app.ico'   # multi-size ICO (16/32/48/64/128/256, PNG entries; final logo)
$exe = Join-Path $outDir 'Decanter.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32manifest:$manifest" "/win32icon:$icon" "/resource:$icon,decanter.ico" "/resource:$src\App\decanter-256.png,decanter256.png" $refs $core $app
if ($LASTEXITCODE -ne 0) { Write-Output ('BUILD FAILED exit=' + $LASTEXITCODE); exit 1 }
Copy-Item (Join-Path $src 'lib\sqlite3.dll') (Join-Path $outDir 'sqlite3.dll') -Force
Copy-Item (Join-Path $src 'App\app.config') (Join-Path $outDir 'Decanter.exe.config') -Force
Write-Output ('BUILD OK -> ' + $exe)
$fi = Get-Item $exe
Write-Output ('exe size: ' + [int]($fi.Length / 1KB) + ' KB')
