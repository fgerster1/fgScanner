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
$newest = @(Get-ChildItem -LiteralPath $toJim -Directory -Filter '*-return' -ErrorAction SilentlyContinue |
  Sort-Object Name | Select-Object -Last 1)
if ($newest.Count -eq 0) {
  Write-Host 'NOT YET - nothing from Franz has arrived yet. Try again later, or call Franz.'
  Done 3
}

Write-Host "Installing Franz's update ($($newest[0].Name)). Do not open FG Scanner until this says OK."
Write-Host ''
& $cli station apply $newest[0].FullName
$exit = $LASTEXITCODE
if ($exit -eq 0) {
  Write-Host ''
  Write-Host 'Read the OK line to Franz. You can open FG Scanner again.'
}
Done $exit
