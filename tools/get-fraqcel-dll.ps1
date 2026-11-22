# Copies the stock Fraqcel 2.15.0 DLLs into ..\lib after checking their SHA-256 hashes.
#
#   powershell -ExecutionPolicy Bypass -File tools\get-fraqcel-dll.ps1 [-Source C:\fraqcel]
#
# The source folder is only read. A file whose hash is not the known 2.15.0 hash is refused.
param(
    [string]$Source = 'C:\fraqcel'
)

$ErrorActionPreference = 'Stop'

$known = @{
    'FraqcelLib2150_x64.dll' = '51c575eabbb5757eae7046440600b041debabc332f64482fb28903152ab8cf46'
    'FraqcelLib2150_x86.dll' = '6778ced499e0cefc87103ef9a7196625dcae5e6e2f5fb1bb0bed1d61dda4cfad'
}

$lib = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
New-Item -ItemType Directory -Force -Path $lib | Out-Null

foreach ($name in $known.Keys) {
    $src = Join-Path $Source $name
    if (-not (Test-Path $src)) { throw "Not found: $src" }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $src).Hash.ToLowerInvariant()
    if ($hash -ne $known[$name]) { throw "$src has sha256 $hash, not the known Fraqcel 2.15.0 hash. Not copied." }
    $dst = Join-Path $lib $name
    Copy-Item -Path $src -Destination $dst -Force
    $check = (Get-FileHash -Algorithm SHA256 -Path $dst).Hash.ToLowerInvariant()
    if ($check -ne $known[$name]) { throw "Copy of $name does not match after copying." }
    Write-Host "ok  $name  $hash"
}
