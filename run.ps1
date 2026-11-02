$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'run\Decanter.exe'
if (-not (Test-Path $exe)) {
    & (Join-Path $root 'build.ps1')
}
Start-Process -FilePath $exe -ArgumentList '--ui=main' -WorkingDirectory (Join-Path $root 'run')
