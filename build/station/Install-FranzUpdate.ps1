<#
.SYNOPSIS
  The "Install Franz's update" desktop shortcut (SPEC-SCANNER-2026-010 section 03, item 3).

.DESCRIPTION
  Installs the newest return in the to-jim folder of the shared Google Drive folder. Folder names
  start with the station and the time they were made, so the newest sorts last. `station apply`
  checks everything itself and changes nothing when it refuses; it says NOT YET while Drive is
  still downloading.

  Windows PowerShell 5.1. ASCII only: 5.1 reads a script without a BOM as ANSI.
#>
$ErrorActionPreference = 'Stop'

# The verb prints middle dots and dashes; a console left on its OEM code page shows them as junk.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Done([int] $exit) {
  # Wait so Jim can read the line out; a run with no console (a script, a remote session) cannot.
  if (-not [Console]::IsInputRedirected) {
    Write-Host ''
    Write-Host 'Press any key to close this window.'
    [void] [Console]::ReadKey($true)
  }
  exit $exit
}

$cli = Join-Path $PSScriptRoot '..\cli\fgscanner.exe'
$setting = Join-Path $PSScriptRoot 'drive-folder.txt'
if (-not (Test-Path -LiteralPath $setting)) {
  Write-Host 'NOT OK - the Google Drive folder was never set up on this laptop - call Franz'
  Done 1
}

$toJim = Join-Path (Get-Content -LiteralPath $setting -Raw).Trim() 'to-jim'
$returns = @(Get-ChildItem -LiteralPath $toJim -Directory -Filter '*-return' -ErrorAction SilentlyContinue)
if ($returns.Count -eq 0) {
  Write-Host 'NOT YET - nothing from Franz has arrived yet. Try again later, or call Franz.'
  Done 3
}

# Two returns are either a superseded one or one that should not be there; install neither
# (Revision D, D7).
if ($returns.Count -gt 1) {
  Write-Host 'NOT OK - more than one update is waiting - call Franz'
  Done 1
}

$pack = $returns[0].FullName
Write-Host "Checking Franz's update ($($returns[0].Name)). Do not open FG Scanner until this says OK."
Write-Host ''
$check = @(& $cli station verify $pack)
$exit = $LASTEXITCODE
$check | ForEach-Object { Write-Host $_ }
if ($exit -ne 0) { Done $exit }

# Nothing changes until Jim has heard Franz read the same code (Revision D, D3): the shared
# folder proves a pack is intact, not who put it there.
$code = ($check | Select-String -Pattern '^OK ([0-9a-f]{4}-[0-9a-f]{4})' | Select-Object -First 1).Matches[0].Groups[1].Value
if ([Console]::IsInputRedirected) {
  Write-Host 'NOT OK - use the "Install Franz''s update" shortcut, so you can check the code with Franz. Nothing was changed.'
  Done 1
}

Write-Host ''
Write-Host "Franz will read you a code on Teams. Is it  $code  ?"
$answer = Read-Host 'Type Y and press Enter if it is the same (anything else if not)'
if ($answer.Trim() -ine 'Y') {
  Write-Host 'Nothing was changed. Call Franz.'
  Done 1
}

Write-Host ''
& $cli station apply $pack --expect-code $code
$exit = $LASTEXITCODE
if ($exit -eq 0) {
  Write-Host ''
  Write-Host 'Read the OK line to Franz. You can open FG Scanner again.'
}
Done $exit
