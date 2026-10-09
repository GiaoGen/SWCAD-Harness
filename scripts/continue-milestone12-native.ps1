[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone12'
$taskContinuation = Join-Path $taskOutput 'continuation-v2'
if (Test-Path -LiteralPath $taskContinuation) { throw 'Continuation has already been frozen/attempted; no rerun.' }
$taskLedger = Get-Content -LiteralPath (Join-Path $taskOutput 'native-budget.json') -Raw | ConvertFrom-Json
$taskCreation = Get-Content -LiteralPath (Join-Path $taskOutput 'worker-create.json') -Raw | ConvertFrom-Json
if ($taskLedger.NewPartAttempts -ne 1 -or $taskLedger.OpenAttempts -ne 0 -or $taskLedger.DocumentsClosed -ne 1 -or
    $taskLedger.OwnedTitles.Count -ne 0 -or $taskCreation.Creation.Succeeded -ne $true -or $taskCreation.Status -ne 'BLOCKED' -or
    $taskCreation.Error -notlike '*System.IO.IOException*being used by another process*' -or
    -not $taskCreation.NoOwnedDocumentRemains -or -not $taskCreation.OriginalActiveRestored) {
    throw 'Only the preserved closed original from the sharing-lock failure may continue; creation is never repeated.'
}
$taskFirstFreeze = Get-Content -LiteralPath (Join-Path $taskOutput 'native-freeze.json') -Raw | ConvertFrom-Json
foreach ($taskEntry in $taskFirstFreeze.files) {
    $taskSaved = Join-Path (Join-Path $taskOutput 'native-v1-snapshot') $taskEntry.path
    if ((Get-FileHash -LiteralPath ('\\?\' + $taskSaved) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskEntry.sha256) {
        throw "Initial failed source/binary snapshot is not preserved: $($taskEntry.path)"
    }
}
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
$taskDll = Join-Path $taskRoot 'tests/CadHarness.ManagedRecovery.Tests/bin/Release/net8.0-windows/CadHarness.ManagedRecovery.Tests.dll'
$taskFiles = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
foreach ($taskFile in Get-ChildItem -LiteralPath (Split-Path -Parent $taskDll) -File) {
    $taskFiles += $taskFile.FullName.Substring($taskRoot.Length + 1).Replace('\','/')
}
$taskEntries = @($taskFiles | Sort-Object -Unique | ForEach-Object {
    [ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
})
New-Item -ItemType Directory -Path $taskContinuation | Out-Null
$taskSchedule = @('migrate','interrupt','recover-edit','readback')
[ordered]@{schemaVersion='0.3';milestone=12;sourceRevision=2;frozenUtc=[DateTime]::UtcNow.ToString('o');files=$taskEntries;
    template=$taskFirstFreeze.template;schedule=$taskSchedule;initialFailedAttemptPreserved=$true;
    reason='Saved original already created and closed; fix readonly sharing while SW retains a writable handle. No repeated creation; same persistent budget ledger.';
    newPartLimit=2;openCycleLimit=5;additionalParts=0;remainingOpenSlots=5;previousNativeFreezeSha256=(Get-FileHash -LiteralPath (Join-Path $taskOutput 'native-freeze.json') -Algorithm SHA256).Hash.ToLowerInvariant();
    oracle=$taskFirstFreeze.nativeOracle;negatives=$taskFirstFreeze.negatives;providerCalls=0;executableExtensionAlternatives=0
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskContinuation 'native-freeze.json') -Encoding UTF8
(Get-FileHash -LiteralPath (Join-Path $taskContinuation 'native-freeze.json') -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $taskContinuation 'native-freeze.json.sha256') -Encoding ASCII
$taskRuns = @()
foreach ($taskStep in $taskSchedule) {
    Write-Output "M12 source-v2 independent continuation controller: $taskStep"
    $taskStarted = [DateTime]::UtcNow
    & $taskDotnet $taskDll $taskRoot --worker $taskStep $taskFirstFreeze.template.path 2>&1 | Tee-Object -FilePath (Join-Path $taskContinuation ('controller-' + $taskStep + '.log'))
    $taskExit = $LASTEXITCODE
    $taskRuns += [ordered]@{step=$taskStep;exitCode=$taskExit;startedUtc=$taskStarted.ToString('o');endedUtc=[DateTime]::UtcNow.ToString('o')}
    if ($taskExit -ne 0) { break }
}
$taskStatus = if ($taskRuns.Count -eq 4 -and @($taskRuns | Where-Object {$_.exitCode -ne 0}).Count -eq 0) { 'COMPLETE' } else { 'PARTIAL' }
[ordered]@{milestone=12;status=$taskStatus;runs=$taskRuns;allAttemptsPreserved=$true;initialSharingFailureRetained=$true;creationReruns=0;budgetLedger='artifacts/milestone12/native-budget.json'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskContinuation 'native-schedule-result.json') -Encoding UTF8
if ($taskStatus -ne 'COMPLETE') { throw 'Continuation acceptance incomplete; preserve failure and stop without further native slots.' }
Write-Output 'M12 continuation completed; initial failure remains in original namespace and cumulative ledger.'
