$ErrorActionPreference = 'Stop'

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$csc = Join-Path $fw 'csc.exe'
$out = Join-Path $PSScriptRoot 'AramOverlayConsole.exe'

$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Web.Extensions.dll'),
    (Join-Path $fw 'System.Net.Http.dll'),
    (Join-Path $fw 'System.Management.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw 'System.Xaml.dll')
)

$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object { $_.Name -ne 'hello.cs' } | ForEach-Object { $_.FullName }

$cscArgs = @('/nologo', '/target:exe', '/platform:anycpu', '/langversion:5', '/optimize+', ('/out:' + $out))
foreach ($ref in $refs) { $cscArgs += ('/reference:' + $ref) }
foreach ($src in $sources) { $cscArgs += $src }

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
Write-Host "Built: $out"
