[CmdletBinding()]
param([switch]$UserAuthorizedAdditionalOpen)
$ErrorActionPreference = 'Stop'
if (-not $UserAuthorizedAdditionalOpen) { throw 'Explicit human authorization for one additional open cycle is required.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCommon = Join-Path $taskRoot 'artifacts/milestone12'
$taskOutput = Join-Path $taskCommon 'authorized-v3'
if (Test-Path -LiteralPath $taskOutput) { throw 'Authorized source-v3 namespace already attempted; no hidden rerun.' }
$taskLedger = Get-Content -LiteralPath (Join-Path $taskCommon 'native-budget.json') -Raw | ConvertFrom-Json
if ($taskLedger.NewPartAttempts -ne 1 -or $taskLedger.OpenAttempts -ne 1 -or $taskLedger.DocumentsClosed -ne 2 -or $taskLedger.OwnedTitles.Count -ne 0) {
    throw 'Cumulative native ledger differs from the exact additional-open authorization.'
}
$taskTemplate = (Get-Content -LiteralPath (Join-Path $taskCommon 'native-freeze.json') -Raw | ConvertFrom-Json).template
foreach ($taskOriginal in Get-Content -LiteralPath (Join-Path $taskCommon 'original-hashes.json') -Raw | ConvertFrom-Json) {
    if ((Get-FileHash -LiteralPath $taskOriginal.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskOriginal.Sha256) { throw 'Originals drifted before authorized source-v3 run.' }
}
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
$taskDll = Join-Path $taskRoot 'tests/CadHarness.ManagedRecovery.Tests/bin/Release/net8.0-windows/CadHarness.ManagedRecovery.Tests.dll'
$taskFiles = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
foreach ($taskFile in Get-ChildItem -LiteralPath (Split-Path -Parent $taskDll) -File) { $taskFiles += $taskFile.FullName.Substring($taskRoot.Length+1).Replace('\','/') }
$taskEntries = @($taskFiles | Sort-Object -Unique | ForEach-Object { [ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant()} })
New-Item -ItemType Directory -Path $taskOutput | Out-Null
[ordered]@{schemaVersion='0.3';userAuthorized=$true;userReply='Authorize one additional open cycle and complete verification';
    grantedUtc=[DateTime]::UtcNow.ToString('o');addedOpenCycles=1;previousOpenCycleLimit=5;newOpenCycleLimit=6;newPartLimit=2;
    priorOpenAttempts=1;priorNewPartAttempts=1;newEvidenceNamespace='artifacts/milestone12/authorized-v3';allPriorFailuresPreserved=$true
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskOutput 'authorization.json') -Encoding UTF8
$taskSchedule = @('migrate','interrupt','recover-edit','readback')
[ordered]@{schemaVersion='0.3';milestone=12;sourceRevision=3;frozenUtc=[DateTime]::UtcNow.ToString('o');files=$taskEntries;template=$taskTemplate;
    schedule=$taskSchedule;authorizationSha256=(Get-FileHash -LiteralPath (Join-Path $taskOutput 'authorization.json') -Algorithm SHA256).Hash.ToLowerInvariant();
    commonLedger='artifacts/milestone12/native-budget.json';newPartsThisSchedule=0;openCyclesThisSchedule=5;cumulativeOpenLimit=6;
    priorFailedReports=@('artifacts/milestone12/worker-create.json','artifacts/milestone12/worker-migrate.json');
    oracle='100x60 plate; four diameter6 through holes at (+/-30,+/-15); native depth8 ->10; definition/extents/volume/positions/boundaries and all five relations';
    failureInjection='AfterNativeSave before state or manifest publication';providerCalls=0;executableExtensionAlternatives=0;creationReruns=0
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutput 'native-freeze.json') -Encoding UTF8
(Get-FileHash -LiteralPath (Join-Path $taskOutput 'native-freeze.json') -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $taskOutput 'native-freeze.json.sha256') -Encoding ASCII
$env:CAD_HARNESS_M12_EVIDENCE_DIR = $taskOutput
$taskRuns = @()
try {
    foreach ($taskStep in $taskSchedule) {
        Write-Output "M12 explicitly authorized source-v3 controller: $taskStep"
        $taskStarted = [DateTime]::UtcNow
        & $taskDotnet $taskDll $taskRoot --worker $taskStep $taskTemplate.path 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput ('controller-' + $taskStep + '.log'))
        $taskExit = $LASTEXITCODE
        $taskRuns += [ordered]@{step=$taskStep;exitCode=$taskExit;startedUtc=$taskStarted.ToString('o');endedUtc=[DateTime]::UtcNow.ToString('o')}
        if ($taskExit -ne 0) { break }
    }
} finally { Remove-Item Env:CAD_HARNESS_M12_EVIDENCE_DIR -ErrorAction SilentlyContinue }
$taskStatus = if ($taskRuns.Count -eq 4 -and @($taskRuns | Where-Object {$_.exitCode -ne 0}).Count -eq 0) { 'COMPLETE' } else { 'PARTIAL' }
[ordered]@{milestone=12;status=$taskStatus;runs=$taskRuns;allAttemptsPreserved=$true;priorFailures=2;explicitAdditionalOpenCycles=1;budgetLedger='artifacts/milestone12/native-budget.json'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'native-schedule-result.json') -Encoding UTF8
if ($taskStatus -ne 'COMPLETE') { throw 'Authorized schedule incomplete. Stop, retain all failures, and never retry or increase budget implicitly.' }
Write-Output 'M12 authorized native schedule completed; verify all reports, original hashes and cumulative lifecycle ledger.'
