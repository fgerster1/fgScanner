# Checks a published payload before it is signed and packed. Release 0.5.2 shipped with the
# command-line tool's fgscanner.exe/.dll/.runtimeconfig.json written over the app's FgScanner.*
# files — the names differ only in case, and Windows file names do not — so the installer held no
# app at all, and every local check passed because local builds never publish the CLI.
#
#   pwsh build/verify-publish.ps1 [-Dir publish/win-x64]
param([string]$Dir = 'publish/win-x64')

$ErrorActionPreference = 'Stop'
$failures = @()

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failures += $what }
}

$exe = Get-ChildItem $Dir -File -Filter 'FgScanner.exe' | Where-Object Name -CEQ 'FgScanner.exe'
Check ($null -ne $exe) "the app is FgScanner.exe, not the CLI's fgscanner.exe"

$config = Join-Path $Dir 'FgScanner.runtimeconfig.json'
Check ((Test-Path $config) -and ((Get-Content $config -Raw) -match '"includedFrameworks"')) `
    'the app carries its own .NET (a self-contained runtimeconfig)'

$cli = Join-Path $Dir 'cli/fgscanner.exe'
Check (Test-Path $cli) 'the CLI is in cli\'

# Beside the app's bundled hostfxr, the CLI's host takes the app folder for a .NET install and
# finds no framework there. In its own folder it finds the machine's.
Check (-not (Test-Path (Join-Path $Dir 'cli/hostfxr.dll'))) 'no bundled .NET beside the CLI'

$naps2 = @(Get-ChildItem $Dir -File -Filter 'NAPS2.*.dll')
Check ($naps2.Count -ge 6) "NAPS2 assemblies separate (LGPL): $($naps2.Count) found"

if ($failures.Count -gt 0) {
    Write-Error "$($failures.Count) check(s) failed in $Dir"
    exit 1
}
