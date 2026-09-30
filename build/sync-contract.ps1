# Sync the index-package contract from JimsStuff into this repo's vendored
# copy (SPEC-2026-005 AC-6). ONE-WAY: JimsStuff docs/contract/ is the law's
# home; docs/contract-vendored/ here is a synced copy that is never edited by
# hand. The script writes the same sync-manifest.json to BOTH repos, and a
# hash test in each suite (tests/test_contract_sync.py there,
# ContractSyncTests.cs here) turns any drift into a red build.
#
#   pwsh -File build/sync-contract.ps1
#   pwsh -File build/sync-contract.ps1 -Source C:\path\to\JimsStuff\docs\contract
[CmdletBinding()]
param(
    [string]$Source
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Source) {
    $Source = Join-Path (Split-Path -Parent $repoRoot) 'JimsStuff\docs\contract'
}
if (-not (Test-Path (Join-Path $Source 'schemas\manifest.schema.json'))) {
    throw "no contract at $Source -- pass -Source pointing at JimsStuff's docs\contract"
}
$target = Join-Path $repoRoot 'docs\contract-vendored'

# Contract content only: the manifest never lists itself, and .gitattributes
# is repo plumbing.
$excluded = @('sync-manifest.json', '.gitattributes')
$files = Get-ChildItem -Path $Source -Recurse -File |
    Where-Object { $excluded -notcontains $_.Name } |
    ForEach-Object {
        $rel = [IO.Path]::GetRelativePath($Source, $_.FullName) -replace '\\', '/'
        [pscustomobject]@{ Rel = $rel; Full = $_.FullName }
    } | Sort-Object { $_.Rel }

# Manifest JSON built by hand: sorted keys, two-space indent, LF, trailing
# newline -- the contract's own byte rules, so both suites can hash the exact
# same bytes without a JSON-formatter disagreement.
$sha = [System.Security.Cryptography.SHA256]::Create()
$lines = @('{', '  "files": {')
$entries = @()
foreach ($f in $files) {
    $digest = ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($f.Full))) -replace '-', '').ToLowerInvariant()
    $entries += ('    "{0}": "{1}"' -f $f.Rel, $digest)
}
$lines += ($entries -join ",`n")
$lines += @('  }', '}')
$manifestText = ($lines -join "`n") + "`n"

# Mirror: start clean so a file deleted from the contract disappears from the
# vendored copy too.
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
foreach ($f in $files) {
    $dest = Join-Path $target ($f.Rel -replace '/', '\')
    New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
    Copy-Item $f.Full $dest
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $target 'sync-manifest.json'), $manifestText, $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $Source 'sync-manifest.json'), $manifestText, $utf8NoBom)

Write-Host ("synced {0} contract file(s)" -f $files.Count)
Write-Host "  from: $Source"
Write-Host "  to:   $target"
Write-Host "  sync-manifest.json written to both repos -- commit both"
