param(
    [string]$Path = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Write-Host "Unblocking: $Path"
Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    try { Unblock-File -LiteralPath $_.FullName } catch { }
}
if (Test-Path -LiteralPath $Path -PathType Leaf) {
    try { Unblock-File -LiteralPath $Path } catch { }
}
Write-Host "Done. If the zip itself is blocked, right-click it -> Properties -> Unblock first."
