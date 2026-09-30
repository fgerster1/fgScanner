# Sync the index-package contract from JimsStuff into this repo's vendored
# copy (JimsStuff SPEC-2026-005 contract-slice, AC-6). ONE-WAY: JimsStuff docs/contract/ is the law's
# home; docs/contract-vendored/ here is a synced copy that is never edited by
# hand. The script writes the same sync-manifest.json to BOTH repos. Each
# suite's per-repo hash test (tests/test_contract_sync.py there,
# ContractSyncTests.cs here) reds an edit made without re-syncing; the
# cross-repo manifest comparison in both suites catches the case a per-repo
# check cannot see — a vendor gone stale together with its own manifest —
# wherever the two repos sit side by side, so commit BOTH repos after a sync.
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
# Normalise BOTH sides before comparing: a trailing separator, an 8.3
# short name or a relative spelling must not slip past this guard (a
# reversed -Source would otherwise delete the tree it is also reading
# from -- which a live probe confirmed). Any Source inside this repo is
# refused outright: the sync is one-way FROM JimsStuff.
$sourceFull = ([IO.Path]::GetFullPath((Convert-Path -LiteralPath $Source))).TrimEnd('\', '/')
$repoFull = ([IO.Path]::GetFullPath($repoRoot)).TrimEnd('\', '/')
if ($sourceFull.StartsWith($repoFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "-Source is inside this repo ($sourceFull) -- the sync is one-way FROM JimsStuff"
}
$Source = $sourceFull

# Contract content only: the manifest never lists itself, .gitattributes is
# repo plumbing, and Thumbs.db/desktop.ini are Explorer droppings — excluded
# here AND in both drift tests, and listed with -Force so a hidden dropping
# is excluded rather than invisible (invisible-to-the-sync but visible-to-
# the-tests was a red no re-sync could clear).
$excluded = @('sync-manifest.json', '.gitattributes', 'Thumbs.db', 'desktop.ini')
$files = @(Get-ChildItem -Path $Source -Recurse -File -Force |
    Where-Object { $excluded -notcontains $_.Name } |
    ForEach-Object {
        $rel = [IO.Path]::GetRelativePath($Source, $_.FullName) -replace '\\', '/'
        [pscustomobject]@{ Rel = $rel; Full = $_.FullName }
    })
# Ordinal, not culture, sort: the manifest's bytes must not depend on the
# machine's locale tables, and code-point order is the folder's own
# json.dumps(sort_keys=True) convention.
[Array]::Sort($files, [System.Comparison[object]]{
    param($a, $b) [string]::CompareOrdinal($a.Rel, $b.Rel) })

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

# Mirror via stage-then-swap: the new tree is built beside the target and
# only swapped in whole, so a mid-script failure leaves the existing
# vendored copy untouched instead of half-deleted. -LiteralPath
# throughout: [ ] in a file name must never act as a wildcard.
$staging = "$target.staging"
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
foreach ($f in $files) {
    $dest = Join-Path $staging ($f.Rel -replace '/', '\')
    New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $f.Full -Destination $dest
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $staging 'sync-manifest.json'), $manifestText, $utf8NoBom)
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Move-Item -LiteralPath $staging -Destination $target
[IO.File]::WriteAllText((Join-Path $Source 'sync-manifest.json'), $manifestText, $utf8NoBom)

Write-Host ("synced {0} contract file(s)" -f $files.Count)
Write-Host "  from: $Source"
Write-Host "  to:   $target"
Write-Host "  sync-manifest.json written to both repos -- commit both"
