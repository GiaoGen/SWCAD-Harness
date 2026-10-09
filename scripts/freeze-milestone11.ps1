[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone11'
$taskManifest = Join-Path $taskOutput 'baseline.json'
if (Test-Path -LiteralPath $taskManifest) { throw 'M11 baseline already exists; do not replace a freeze.' }
$taskTracked = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate baseline source.' }
# The freeze helper is new; all pre-existing tracked files and the supplied PRD are frozen.
$taskPaths = @($taskTracked + 'SWCAD_Harness_v0.3_PRD.md' | Sort-Object -Unique)
$taskEntries = @($taskPaths | ForEach-Object {
    $taskFile = Join-Path $taskRoot $_
    [ordered]@{ path = $_.Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant(); category = 'source' }
})
$taskHistory = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'artifacts') -Directory | Where-Object Name -Like 'milestone*' | Where-Object Name -NE 'milestone11' | Get-ChildItem -File -Recurse)
$taskBinaries = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter 'CadHarness*.dll' -File -Recurse | Where-Object FullName -Match '[\\/]bin[\\/]')
foreach ($taskFile in @($taskHistory + $taskBinaries)) {
    $taskEntries += [ordered]@{ path = $taskFile.FullName.Substring($taskRoot.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); category = $(if ($taskFile.FullName -Match '[\\/]bin[\\/]') { 'binary' } else { 'historical_evidence' }) }
}
$taskData = [ordered]@{ schemaVersion = '0.3'; milestone = 11; frozenAtUtc = [DateTime]::UtcNow.ToString('o'); head = (& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot rev-parse HEAD); workingTree = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot status --short); completionBaseline = 'docs/milestone-10-formal-verification.md'; entries = $taskEntries }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskData | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskManifest -Encoding UTF8
(Get-FileHash -LiteralPath $taskManifest -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($taskManifest + '.sha256') -Encoding ASCII
Write-Output "M11 baseline frozen: $($taskEntries.Count) files; no native calls."
