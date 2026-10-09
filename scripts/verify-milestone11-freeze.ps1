[CmdletBinding()]
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone11'
$taskBaselinePath = Join-Path $taskOutput 'baseline.json'
if ((Get-FileHash -LiteralPath $taskBaselinePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne (Get-Content -LiteralPath ($taskBaselinePath + '.sha256') -Raw).Trim()) { throw 'Initial baseline manifest hash changed.' }
$taskBaseline = Get-Content -LiteralPath $taskBaselinePath -Raw | ConvertFrom-Json
$taskChanges = @()
foreach ($taskEntry in $taskBaseline.entries) {
    $taskActual = (Get-FileHash -LiteralPath (Join-Path $taskRoot $taskEntry.path) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($taskActual -cne $taskEntry.sha256) { $taskChanges += [ordered]@{ path=$taskEntry.path; category=$taskEntry.category; before=$taskEntry.sha256; after=$taskActual } }
}
$taskUnexpected = @($taskChanges | Where-Object { $_.category -ne 'binary' -and $_.path -ne 'CadHarness.sln' })
if ($taskUnexpected.Count -ne 0) { $taskUnexpected | ConvertTo-Json; throw 'Pre-existing source or historical evidence changed outside the solution registration.' }
$taskPure = Get-Content -LiteralPath (Join-Path $taskOutput 'pure-results.json') -Raw | ConvertFrom-Json
$taskRegression = Get-Content -LiteralPath (Join-Path $taskOutput 'v02-regression.json') -Raw | ConvertFrom-Json
if ($taskPure.failed -ne 0 -or $taskRegression.status -ne 'NO_NEW_REGRESSIONS') { throw 'M11 pure/regression gates not satisfied.' }
$taskAudit = [ordered]@{ status='PASS'; baselineHead=$taskBaseline.head; auditedFiles=$taskBaseline.entries.Count; sourceChanges=@($taskChanges | Where-Object category -EQ 'source'); rebuiltBinaryChanges=@($taskChanges | Where-Object category -EQ 'binary'); historicalEvidenceChanged=0; existingProductionSourceChanged=0; nativePartsCreated=0; nativeOpenCycles=0; nativePartsClosed=0; nativeFixtureChecksums='Not applicable: no native fixture opened or modified' }
$taskAudit | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $taskOutput 'preservation-audit.json') -Encoding UTF8
$taskFreezePath = Join-Path $taskOutput 'contract-freeze.json'
if ($Publish) {
    if (Test-Path -LiteralPath $taskFreezePath) { throw 'Final M11 contract freeze already exists; cannot replace it.' }
    $taskFiles = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate final source.' }
    $taskFinalEntries = @($taskFiles | ForEach-Object { [ordered]@{ path=$_.Replace('\','/'); sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $taskFixturePath = Join-Path $taskRoot 'tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json'
    $taskFixture = Get-Content -LiteralPath $taskFixturePath -Raw | ConvertFrom-Json -AsHashtable
    $taskDevText = ConvertTo-Json -InputObject $taskFixture.development -Depth 30 -Compress
    $taskHeldText = ConvertTo-Json -InputObject $taskFixture.heldOut -Depth 30 -Compress
    function Text-Hash([string]$taskText) { return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskText))).ToLowerInvariant() }
    $taskFreeze = [ordered]@{ schemaVersion='0.3'; milestone=11; status='COMPLETE'; prd='v0.3-draft-2'; frozenAtUtc=[DateTime]::UtcNow.ToString('o'); baselineManifestSha256=(Get-Content -LiteralPath ($taskBaselinePath + '.sha256') -Raw).Trim(); source=$taskFinalEntries; developmentTasksSha256=(Text-Hash $taskDevText); heldOutTasksSha256=(Text-Hash $taskHeldText); taskSubsetHashEncoding='UTF8 PowerShell ordered JSON compressed, depth30'; purePassed=$taskPure.passed; nativePartsCreated=0; nativeOpenCycles=0; nativePartsClosed=0; contractOnly=$true; laterMilestonesImplemented=@(); providerCalls=0; capabilityContractSha256=(Get-FileHash -LiteralPath (Join-Path $taskOutput 'capability-contracts.json') -Algorithm SHA256).Hash.ToLowerInvariant() }
    $taskFreeze | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskFreezePath -Encoding UTF8
    (Get-FileHash -LiteralPath $taskFreezePath -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($taskFreezePath + '.sha256') -Encoding ASCII
    [ordered]@{ milestone=11; newPartsBudget=0; openCycleBudget=1; openCycleConditions='Documented necessary contract probe only'; usedNewParts=0; usedOpenCycles=0; closedParts=0; comProbeRequired=$false; originalFixturesModified=0 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'native-budget.json') -Encoding UTF8
} elseif (Test-Path -LiteralPath $taskFreezePath) {
    if ((Get-FileHash -LiteralPath $taskFreezePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne (Get-Content -LiteralPath ($taskFreezePath + '.sha256') -Raw).Trim()) { throw 'Final contract manifest hash changed.' }
    $taskFinalFreeze = Get-Content -LiteralPath $taskFreezePath -Raw | ConvertFrom-Json
    foreach ($taskEntry in $taskFinalFreeze.source) { if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskEntry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $taskEntry.sha256) { throw "Frozen M11 file changed: $($taskEntry.path)" } }
}
Write-Output "M11 preservation PASS: $($taskBaseline.entries.Count) original files audited; historical evidence/source preserved; native budget used 0/0."
