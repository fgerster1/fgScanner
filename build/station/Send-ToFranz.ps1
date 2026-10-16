<#
.SYNOPSIS
  The "Send my scans to Franz" desktop shortcut (SPEC-SCANNER-2026-010 §03 item 3).

.DESCRIPTION
  Packs this station into the to-franz folder of the shared Google Drive folder, based on the
  last pack both sides share, so only new and changed files travel. The Drive folder is the one
  written at setup into drive-folder.txt beside this script - never guessed.

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

$drive = (Get-Content -LiteralPath $setting -Raw).Trim()
$arguments = @('station', 'pack', '--kind', 'send', '--station', 'jim', '--to', (Join-Path $drive 'to-franz'))
$baseline = Join-Path (Join-Path $env:APPDATA 'FGScanner') 'station-sent-manifest.json'
if (Test-Path -LiteralPath $baseline) {
  $arguments += @('--based-on', $baseline)
}

Write-Host 'Packing your scans for Franz. This can take a few minutes - leave the laptop on.'
Write-Host ''
& $cli @arguments
$exit = $LASTEXITCODE
if ($exit -eq 0) {
  Write-Host ''
  Write-Host 'Read the OK line to Franz. Leave the laptop on until Google Drive says it is done.'
}
Done $exit
