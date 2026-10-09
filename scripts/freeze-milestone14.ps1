[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts/milestone14'
if (Test-Path -LiteralPath (Join-Path $output 'baseline.json')) { throw 'M14 baseline already exists; never overwrite.' }
function Hash-File([string]$path) {
    $s = [IO.File]::OpenRead('\\?\' + [IO.Path]::GetFullPath($path))
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant() } finally { $s.Dispose() }
}
$files = @(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate baseline.' }
$entries = @($files | ForEach-Object { [ordered]@{path=$_.Replace('\','/');sha256=(Hash-File (Join-Path $root $_));category='source'} })
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -File -Recurse | Where-Object {$_.FullName -notlike '*\milestone14\*'}) {
    $entries += [ordered]@{path=$file.FullName.Substring($root.Length+1).Replace('\','/');sha256=(Hash-File $file.FullName);category='historical_evidence'}
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
[ordered]@{milestone=14;prd='v0.3-draft-2';utc=[DateTime]::UtcNow.ToString('o');entries=$entries;
    head=(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root rev-parse HEAD);
    workingTree=@(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root status --short)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'baseline.json') -Encoding UTF8
Hash-File (Join-Path $output 'baseline.json') | Set-Content -LiteralPath (Join-Path $output 'baseline.json.sha256') -Encoding ASCII
[ordered]@{Milestone=14;MaximumNewParts=0;MaximumOpenCycles=12;OpenAttempts=0;DocumentsClosed=0;AttemptedSteps=@();OwnedTitles=@();Events=@()} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'native-budget.json') -Encoding UTF8
Write-Output "M14 baseline frozen: $($entries.Count) files; native use 0 Parts / 0 opens."
