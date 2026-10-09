[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone12'
$taskTarget = Join-Path $taskOutput 'baseline.json'
if (Test-Path -LiteralPath $taskTarget) { throw 'M12 baseline exists; never overwrite it.' }
$taskM11 = Get-Content -LiteralPath (Join-Path $taskRoot 'artifacts/milestone11/contract-freeze.json') -Raw | ConvertFrom-Json
foreach ($taskEntry in $taskM11.source) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskEntry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $taskEntry.sha256) { throw "M11 source drift: $($taskEntry.path)" }
}
$taskFiles = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
$taskEntries = @($taskFiles | ForEach-Object { [ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant();category='source'} })
foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'artifacts') -Directory | Where-Object { $_.Name -like 'milestone*' -and $_.Name -ne 'milestone12' } | Get-ChildItem -File -Recurse) {
    $taskEntries += [ordered]@{path=$taskFile.FullName.Substring($taskRoot.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant();category='historical_evidence'}
}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
[ordered]@{schemaVersion='0.3';milestone=12;prd='v0.3-draft-2';frozenAtUtc=[DateTime]::UtcNow.ToString('o');head=(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot rev-parse HEAD);workingTree=@(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot status --short);entries=$taskEntries} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskTarget -Encoding UTF8
(Get-FileHash -LiteralPath $taskTarget -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($taskTarget+'.sha256') -Encoding ASCII
Write-Output "M12 baseline frozen: $($taskEntries.Count) files; native use 0/0."
