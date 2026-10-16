<#
.SYNOPSIS
  Jim's first send (SPEC-SCANNER-2026-010 cycle 1): packs his FG Scanner 0.5.4 station into the
  to-franz folder of the shared Google Drive folder.

.DESCRIPTION
  0.5.4 has no station verbs, so this is a PowerShell port of `fgscanner station pack` for a first
  pack only (everything ships; no basedOn). Its manifest must be byte-identical to what the C#
  verb writes for the same station - StationSenderTests checks exactly that - so the code Jim
  reads out is the code Franz's `station verify` prints.

  It cannot read SQLite, so two things the verb checks are not checked here: reading jobs still
  pending (0.5.4 on Jim's laptop reads nothing in the background) and the last migration, which is
  fixed for 0.5.4. The database is copied whole with the app closed, which is consistent because
  0.5.4 keeps its journal in `delete` mode.

  Windows PowerShell 5.1 (Jim's laptop). ASCII only: 5.1 reads a script without a BOM as ANSI.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Send-ToFranz-0.5.4.ps1 -To "G:\My Drive\FGScanner-Jim\to-franz"
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $To,
  [string] $StationRoot = (Join-Path $env:APPDATA 'FGScanner'),
  [string] $EvidenceRoot = 'C:\Evidence-Scans',
  [string] $Station = 'jim',
  [string] $AppVersion,
  [string] $SentAt,
  # For the test suite only: the developer's own FG Scanner may be open while tests run.
  [switch] $NoAppCheck
)

$ErrorActionPreference = 'Stop'
$LastMigration = '20260914210756_AddFieldLengthAndMemo'
$ImageExtensions = @('.jpg', '.jpeg', '.png', '.tif', '.tiff', '.bmp', '.pdf')
$Utf8 = New-Object System.Text.UTF8Encoding $false

function Refuse([string] $why) {
  Write-Output "NOT OK - $why - call Franz"
  exit 1
}

function Sha256([string] $path) {
  # .NET directly, not Get-FileHash: a module that fails to autoload would stop the send.
  $stream = [System.IO.File]::OpenRead($path)
  try {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    (($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '')
  }
  finally { $stream.Dispose() }
}

function Esc([string] $s) {
  # The escaping System.Text.Json's UnsafeRelaxedJsonEscaping does for file names: quote,
  # backslash and control characters; everything else, accents included, stays as it is.
  $b = New-Object System.Text.StringBuilder
  foreach ($ch in $s.ToCharArray()) {
    $c = [int] $ch
    if ($ch -eq '"') { [void] $b.Append('\"') }
    elseif ($ch -eq '\') { [void] $b.Append('\\') }
    elseif ($c -lt 0x20) { [void] $b.Append(('\u{0:X4}' -f $c)) }
    else { [void] $b.Append($ch) }
  }
  $b.ToString()
}

function KindOf([string] $prefix, [string] $rel) {
  if ($prefix -eq 'trash') { return 'trash' }
  if (@($rel.Split('/') | Where-Object { $_ -ieq 'originals' }).Count -gt 0) { return 'original' }
  $name = $rel.Substring($rel.LastIndexOf('/') + 1)
  if ($name.StartsWith('index.', [System.StringComparison]::OrdinalIgnoreCase) -or $name -ieq 'manifest.json') { return 'index' }
  $ext = [System.IO.Path]::GetExtension($name)
  if (@($ImageExtensions | Where-Object { $_ -ieq $ext }).Count -gt 0) { return 'image' }
  'sidecar'
}

function FilesUnder([string] $root, [string] $prefix) {
  if (-not (Test-Path -LiteralPath $root)) { return }
  $full = [System.IO.Path]::GetFullPath($root).TrimEnd('\')
  Get-ChildItem -LiteralPath $full -Recurse -File -Force | ForEach-Object {
    $rel = $prefix + '/' + $_.FullName.Substring($full.Length + 1).Replace('\', '/')
    [pscustomobject] @{ Rel = $rel; Path = $_.FullName; Size = $_.Length; Sha256 = (Sha256 $_.FullName); Kind = (KindOf $prefix $rel) }
  }
}

function IsInside([string] $path, [string] $folder) {
  $p = [System.IO.Path]::GetFullPath($path).TrimEnd('\')
  $f = [System.IO.Path]::GetFullPath($folder).TrimEnd('\')
  ($p -ieq $f) -or $p.StartsWith($f + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

# ---- the station must be 0.5.4 and at rest ----
if (-not $AppVersion) {
  $exe = 'C:\Program Files\FGScanner\FgScanner.exe'
  if (-not (Test-Path -LiteralPath $exe)) { Refuse "FG Scanner is not installed at $exe" }
  $AppVersion = ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]
}
if ($AppVersion -ne '0.5.4') { Refuse "this sender is for FG Scanner 0.5.4, and this laptop has $AppVersion" }

$database = Join-Path $StationRoot 'fgscanner.db'
if (-not (Test-Path -LiteralPath $database)) { Refuse "there is no FG Scanner database at $database" }
# FG Scanner's own single-instance lock is held for the whole send, so the app cannot open and
# scan into a station that is half copied (Revision D, D5).
$hold = $null
if (-not $NoAppCheck) {
  $ours = $false
  if (@(Get-Process -Name FgScanner -ErrorAction SilentlyContinue).Count -eq 0) {
    $hold = New-Object System.Threading.Mutex($true, 'FGScanner.SingleInstance', [ref] $ours)
  }
  if (-not $ours) { Refuse 'FG Scanner is open - close it, then send again' }
}
$wal = "$database-wal"
if ((Test-Path -LiteralPath "$database-journal") -or ((Test-Path -LiteralPath $wal) -and (Get-Item -LiteralPath $wal).Length -gt 0)) {
  Refuse 'the database was not closed cleanly - start FG Scanner once, close it, then send again'
}
$recovery = Join-Path $StationRoot 'recovery'
if ((Test-Path -LiteralPath $recovery) -and
    @(Get-ChildItem -LiteralPath $recovery -Recurse -File -Force | Where-Object { $_.Name -ne '.lock' }).Count -gt 0) {
  Refuse 'FG Scanner holds unsaved scans - start it, save or discard them, then send again'
}
if (-not (Test-Path -LiteralPath $EvidenceRoot)) { Refuse "the evidence folder $EvidenceRoot does not exist" }
foreach ($live in @($StationRoot, $EvidenceRoot)) {
  if (IsInside $To $live) { Refuse "the Drive folder is inside the station's own folders: $To" }
}
if (-not (Test-Path -LiteralPath $To)) { Refuse "the Drive folder $To does not exist - is Google Drive running?" }

# ---- what the station holds ----
if ($SentAt) { $at = [System.DateTimeOffset]::Parse($SentAt, [System.Globalization.CultureInfo]::InvariantCulture) }
else { $at = [System.DateTimeOffset]::Now }
$files = @(FilesUnder $EvidenceRoot 'evidence') + @(FilesUnder (Join-Path $StationRoot 'trash') 'trash')
$rels = [string[]] @($files | ForEach-Object { $_.Rel })
[System.Array]::Sort($rels, [System.StringComparer]::Ordinal)
$byRel = @{}
foreach ($f in $files) { $byRel[$f.Rel] = $f }

$needed = (Get-Item -LiteralPath $database).Length + ($files | Measure-Object -Property Size -Sum).Sum + 1GB
$free = (New-Object System.IO.DriveInfo ([System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($To)))).AvailableFreeSpace
if ($free -lt $needed) { Refuse ("not enough free space at $To (the pack needs {0:N0} MB, there is {1:N0} MB)" -f ($needed / 1MB), ($free / 1MB)) }

# ---- pack ----
Add-Type -Namespace FgScanner -Name Power -MemberDefinition '[DllImport("kernel32")] public static extern uint SetThreadExecutionState(uint flags);'
[void] [FgScanner.Power]::SetThreadExecutionState([uint32] 2147483649) # ES_CONTINUOUS | ES_SYSTEM_REQUIRED: the laptop stays awake
try {
  $stamp = $at.ToString('yyyy-MM-dd-HHmm', [System.Globalization.CultureInfo]::InvariantCulture)
  $n = 1
  do {
    $name = if ($n -eq 1) { "$Station-$stamp-send" } else { "$Station-$stamp-$n-send" }
    $pack = Join-Path $To $name
    $n++
  } while (Test-Path -LiteralPath $pack)
  New-Item -ItemType Directory -Path $pack | Out-Null

  try {
    $liveDb = Sha256 $database
    Copy-Item -LiteralPath $database -Destination (Join-Path $pack 'fgscanner.db')
    foreach ($rel in $rels) {
      $target = Join-Path $pack ($rel.Replace('/', '\'))
      New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
      Copy-Item -LiteralPath $byRel[$rel].Path -Destination $target
    }

    $evidenceRootFull = [System.IO.Path]::GetFullPath($EvidenceRoot)
    if ($evidenceRootFull.Length -gt 3) { $evidenceRootFull = $evidenceRootFull.TrimEnd('\') }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('{')
    $lines.Add('  "format": 1,')
    $lines.Add('  "station": "' + (Esc $Station) + '",')
    $lines.Add('  "sentAt": "' + $at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture) + '",')
    $lines.Add('  "appVersion": "' + (Esc $AppVersion) + '",')
    $lines.Add('  "lastMigration": "' + $LastMigration + '",')
    $lines.Add('  "dbSha256": "' + (Sha256 (Join-Path $pack 'fgscanner.db')) + '",')
    $lines.Add('  "evidenceRoot": "' + (Esc $evidenceRootFull) + '",')
    if ($rels.Count -eq 0) {
      $lines.Add('  "files": []')
    } else {
      $lines.Add('  "files": [')
      for ($i = 0; $i -lt $rels.Count; $i++) {
        $f = $byRel[$rels[$i]]
        $lines.Add('    {')
        $lines.Add('      "rel": "' + (Esc $f.Rel) + '",')
        $lines.Add('      "size": ' + $f.Size.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ',')
        $lines.Add('      "sha256": "' + $f.Sha256 + '",')
        $lines.Add('      "kind": "' + $f.Kind + '"')
        $lines.Add($(if ($i -lt $rels.Count - 1) { '    },' } else { '    }' }))
      }
      $lines.Add('  ]')
    }
    $lines.Add('}')
    $manifestBytes = $Utf8.GetBytes([string]::Join("`n", $lines))

    # Every copy is re-read before the manifest is written: the manifest is the receipt.
    foreach ($rel in $rels) {
      if ((Sha256 (Join-Path $pack ($rel.Replace('/', '\')))) -ne $byRel[$rel].Sha256) { throw "$rel did not copy intact" }
    }

    $manifest = Join-Path $pack 'station-manifest.json'
    [System.IO.File]::WriteAllBytes("$manifest.tmp", $manifestBytes)
    Move-Item -LiteralPath "$manifest.tmp" -Destination $manifest
  }
  catch {
    Remove-Item -LiteralPath $pack -Recurse -Force -ErrorAction SilentlyContinue
    Refuse "the pack could not be written ($($_.Exception.Message))"
  }

  $sha = New-Object System.Security.Cryptography.SHA256Managed
  $manifestSha = (($sha.ComputeHash($manifestBytes) | ForEach-Object { $_.ToString('x2') }) -join '')
  # Eight characters, as the station verb prints them (Revision D, D3).
  $code = $manifestSha.Substring(0, 4) + '-' + $manifestSha.Substring(4, 4)

  # The freeze record: the next update from Franz is checked against what left here. This
  # sender cannot read SQLite, so it keeps the database it sent; apply fingerprints that copy.
  Copy-Item -LiteralPath (Join-Path $pack 'fgscanner.db') -Destination (Join-Path $StationRoot 'station-sent.db') -Force
  [System.IO.File]::WriteAllBytes((Join-Path $StationRoot 'station-sent-manifest.json.tmp'), $manifestBytes)
  Move-Item -LiteralPath (Join-Path $StationRoot 'station-sent-manifest.json.tmp') -Destination (Join-Path $StationRoot 'station-sent-manifest.json') -Force
  $freeze = "{`r`n  `"pack`": `"$(Esc $name)`",`r`n  `"code`": `"$code`",`r`n  `"sentAt`": `"$($at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture))`",`r`n  `"liveDbSha256`": `"$liveDb`",`r`n  `"manifestSha256`": `"$manifestSha`",`r`n  `"content`": null`r`n}"
  [System.IO.File]::WriteAllBytes((Join-Path $StationRoot 'station-freeze.json.tmp'), $Utf8.GetBytes($freeze))
  Move-Item -LiteralPath (Join-Path $StationRoot 'station-freeze.json.tmp') -Destination (Join-Path $StationRoot 'station-freeze.json') -Force

  Write-Output "OK $code - leave the laptop on until Google Drive says it's done"
  exit 0
}
finally {
  [void] [FgScanner.Power]::SetThreadExecutionState([uint32] 2147483648)
  if ($hold) { $hold.ReleaseMutex(); $hold.Dispose() }
}
