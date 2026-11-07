$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Compile locally so the exe has no Zone.Identifier/MOTW marker and is not
# blocked by "Windows protected your PC" for a downloaded prebuilt exe.
& (Join-Path $root 'build.ps1')

$exe = Join-Path $root 'AramOverlay.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build did not produce $exe"
}
Start-Process -FilePath $exe -WorkingDirectory $root
