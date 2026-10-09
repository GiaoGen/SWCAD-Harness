[CmdletBinding()]
param(
    [string]$PureEvidence = 'artifacts/milestone12/pure/20261009T024016920/results.json',
    [string]$RegressionEvidence = 'artifacts/milestone12/regression/20261009T024018897'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCommon = Join-Path $taskRoot 'artifacts/milestone12'
$taskAccepted = Join-Path $taskCommon 'authorized-v3'
function Hash-File([string]$Path) {
    # Extended paths also address historical files named aux.json on Windows.
    $absolute = [IO.Path]::GetFullPath($Path)
    $nativePath = if ($IsWindows) { '\\?\' + $absolute } else { $absolute }
    $stream = [IO.File]::OpenRead($nativePath)
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
    finally { $stream.Dispose() }
}
function Read-Json([string]$Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Verify-Freeze([string]$Path) {
    Require ((Hash-File $Path) -ceq (Get-Content -LiteralPath ($Path + '.sha256') -Raw).Trim()) "Freeze drift: $Path"
    Read-Json $Path
}
function Verify-Artifact($Artifact) {
    Require ((Hash-File $Artifact.path) -ceq $Artifact.sha256) "Artifact drift: $($Artifact.path)"
}
function Near([double]$Actual, [double]$Expected) {
    Require ([double]::IsFinite($Actual) -and [Math]::Abs($Actual - $Expected) -lt 0.001) "Oracle mismatch: $Actual / $Expected"
}
function Verify-Observation($Observation, [double]$Depth, [int]$Revision) {
    Require ($null -ne $Observation) 'Native observation missing.'
    Near $Observation.DefinitionDepth $Depth
    Near $Observation.Extents.WidthMm 100
    Near $Observation.Extents.HeightMm 60
    Near $Observation.Extents.DepthMm $Depth
    Near $Observation.Volume ((100 * 60 - 4 * [Math]::PI * 9) * $Depth)
    Require ($Observation.Extents.SolidBodyCount -eq 1 -and $Observation.Revision -eq $Revision -and
        $Observation.Relations -eq 5 -and $Observation.Dependencies -eq 6 -and $Observation.Holes.Count -eq 4) 'Native inventory differs.'
    $positions = @(@(-30,-15),@(-30,15),@(30,-15),@(30,15))
    for ($index = 0; $index -lt 4; $index++) {
        $hole = $Observation.Holes[$index]
        Near $hole.X $positions[$index][0]
        Near $hole.Y $positions[$index][1]
        Near $hole.Diameter 6
        Require ($hole.BoundaryZ.Count -eq 2) 'Through-hole boundary inventory differs.'
        Near $hole.BoundaryZ[0] 0
        Near $hole.BoundaryZ[1] $Depth
    }
}
$baseline = Verify-Freeze (Join-Path $taskCommon 'baseline.json')
$changes = @()
foreach ($entry in $baseline.entries) {
    $actual = Hash-File (Join-Path $taskRoot $entry.path)
    if ($actual -cne $entry.sha256) {
        $changes += [ordered]@{path=$entry.path;category=$entry.category;before=$entry.sha256;after=$actual}
    }
}
Require (@($changes | Where-Object {$_.path -ne 'CadHarness.sln' -or $_.category -ne 'source'}).Count -eq 0) 'Existing source or historical evidence changed.'
$m11 = Verify-Freeze (Join-Path $taskRoot 'artifacts/milestone11/contract-freeze.json')
foreach ($entry in $m11.source | Where-Object path -NE 'CadHarness.sln') {
    Require ((Hash-File (Join-Path $taskRoot $entry.path)) -ceq $entry.sha256) "M11 contract/source drift: $($entry.path)"
}
$acceptedFreeze = Verify-Freeze (Join-Path $taskAccepted 'native-freeze.json')
foreach ($entry in $acceptedFreeze.files) {
    Require ((Hash-File (Join-Path $taskRoot $entry.path)) -ceq $entry.sha256) "Accepted source/binary drift: $($entry.path)"
}
Verify-Artifact $acceptedFreeze.template
$authorization = Read-Json (Join-Path $taskAccepted 'authorization.json')
Require ((Hash-File (Join-Path $taskAccepted 'authorization.json')) -ceq $acceptedFreeze.authorizationSha256) 'Authorization hash drift.'
Require ($authorization.userAuthorized -and $authorization.addedOpenCycles -eq 1 -and $authorization.newOpenCycleLimit -eq 6) 'Explicit one-open authorization missing.'
$priorRuns = @(
    @{freeze=(Join-Path $taskCommon 'native-freeze.json');snapshot=(Join-Path $taskCommon 'native-v1-snapshot');report=(Join-Path $taskCommon 'worker-create.json')},
    @{freeze=(Join-Path $taskCommon 'continuation-v2/native-freeze.json');snapshot=(Join-Path $taskCommon 'native-v2-snapshot');report=(Join-Path $taskCommon 'worker-migrate.json')}
)
foreach ($prior in $priorRuns) {
    $freeze = Verify-Freeze $prior.freeze
    foreach ($entry in $freeze.files) {
        Require ((Hash-File (Join-Path $prior.snapshot $entry.path)) -ceq $entry.sha256) "Prior failed source/binary snapshot drift: $($entry.path)"
    }
    $report = Read-Json $prior.report
    Require ($report.Status -ne 'COMPLETE' -and $report.NoOwnedDocumentRemains -and $report.OriginalActiveRestored) 'Prior failed attempt or its cleanup was lost.'
}
$originals = Read-Json (Join-Path $taskCommon 'original-hashes.json')
foreach ($original in $originals) {
    Verify-Artifact $original
    Require ((Get-Item -LiteralPath $original.Path).Length -eq $original.SizeBytes) 'Original size differs.'
}
$schedule = Read-Json (Join-Path $taskAccepted 'native-schedule-result.json')
Require ($schedule.status -eq 'COMPLETE' -and $schedule.runs.Count -eq 4 -and
    @($schedule.runs | Where-Object exitCode -NE 0).Count -eq 0) 'Accepted schedule did not complete.'
$reports = @{}
foreach ($step in @('migrate','interrupt','recover-edit','readback')) {
    $report = Read-Json (Join-Path $taskAccepted ('worker-' + $step + '.json'))
    Require ($report.Status -eq 'COMPLETE' -and $null -eq $report.FailureCode -and
        $report.NoOwnedDocumentRemains -and $report.OriginalActiveRestored -and $report.OriginalsPreserved -and
        -not $report.StartedApplication -and $report.SolidWorksVersion -eq '32.0.1') "Native step failed: $step"
    $reports[$step] = $report
}
$pids = @((Read-Json (Join-Path $taskCommon 'worker-create.json')).ControllerPid) + @($reports.Values | ForEach-Object ControllerPid)
Require (@($pids | Sort-Object -Unique).Count -eq 5) 'Required cold-controller sequence reused a process.'
Require ($reports.migrate.Migration.NativeReopenVerified -and $reports.migrate.Migration.OriginalsPreserved) 'Copy migration lacks native proof.'
Verify-Observation $reports.migrate.After 8 1
$interrupted = $reports.interrupt
Require (-not $interrupted.Edit.Succeeded -and $interrupted.Edit.FailureCode -eq 'INJECTED_INTERRUPTED_PUBLISH' -and
    $interrupted.Edit.RollbackSucceeded -and -not $interrupted.Edit.StateCommitted -and
    $interrupted.PointerBefore -ceq $interrupted.PointerAfter -and $interrupted.JournalRetained -and
    $interrupted.SavedDiskDiffersFromAuthoritative -and $interrupted.RetainedRecoveryMarker.previousRevision -eq 1) 'Interrupted publish authority was not proven.'
Verify-Observation $interrupted.After 8 1
$expectedRefusals = @{
    'native-document-guid'='DOCUMENT_IDENTITY_MISMATCH';'native-configuration-guid'='CONFIGURATION_MISMATCH';
    'native-configuration-name'='CONFIGURATION_MISMATCH';'native-save-as-lineage'='DOCUMENT_IDENTITY_MISMATCH';
    'native-stale-persistent-reference'='STALE_REFERENCE';'native-state-scalar-drift'='RELATION_VIOLATED';
    'dispatch-after-interruption'='INCOMPLETE_DURABLE_PUBLISH'
}
foreach ($key in $expectedRefusals.Keys) {
    Require ($interrupted.Refusals.$key -ceq $expectedRefusals[$key]) "Typed native refusal missing: $key"
}
$edit = $reports['recover-edit']
Require ($edit.Edit.Succeeded -and $edit.Edit.StateCommitted -and $edit.Edit.Revision -eq 2 -and
    $edit.PointerBefore -cne $edit.PointerAfter -and -not $edit.JournalRetained -and $edit.TargetPersistentReferencePreserved) 'Recovered durable edit failed.'
Verify-Observation $edit.Before 8 1
Verify-Observation $edit.After 10 2
Verify-Observation $reports.readback.After 10 2
Require ($reports.readback.Refusals.'actual-native-configuration-switch' -eq 'CONFIGURATION_MISMATCH') 'Actual configuration switch was not refused.'
$package = Join-Path $taskAccepted 'managed'
$pointer = Read-Json (Join-Path $package 'current.json')
Verify-Artifact $pointer.manifest
$manifest = Read-Json $pointer.manifest.path
Require ($manifest.revision -eq 2 -and $manifest.origin -eq 'harness' -and $manifest.stage -eq 'pointer_published') 'Final manifest is not a complete managed revision.'
Verify-Artifact $manifest.nativePart
Verify-Artifact $manifest.state
Verify-Artifact $manifest.program
Require ((Hash-File (Join-Path $package 'working/CADHarnessManagedPart.SLDPRT')) -ceq $manifest.nativePart.sha256) 'Final working bytes do not match the immutable snapshot.'
Require (-not (Test-Path -LiteralPath (Join-Path $package 'recovery.json'))) 'Recovery marker still blocks editing.'
$previous = $interrupted.RetainedRecoveryMarker.previousManifest
Verify-Artifact $previous
Require ($manifest.previousManifestSha256 -ceq $previous.sha256) 'Previous complete revision lineage was lost.'
$previousManifest = Read-Json $previous.path
Verify-Artifact $previousManifest.nativePart
Verify-Artifact $previousManifest.state
Verify-Artifact $previousManifest.program
$ledger = Read-Json (Join-Path $taskCommon 'native-budget.json')
Require ($ledger.NewPartAttempts -eq 1 -and $ledger.OpenAttempts -eq 6 -and $ledger.MaximumOpenCycles -eq 6 -and
    $ledger.DocumentsClosed -eq 7 -and $ledger.OwnedTitles.Count -eq 0 -and $ledger.Authorizations.Count -eq 1) 'Cumulative lifecycle differs from authorized limits or has a leaked document.'
Require (@($ledger.Events | Where-Object Kind -EQ 'reserve-new-part').Count -eq 1 -and
    @($ledger.Events | Where-Object Kind -EQ 'reserve-open').Count -eq 6 -and
    @($ledger.Events | Where-Object Kind -EQ 'closed-owned').Count -eq 7) 'Ledger totals disagree with lifecycle events.'
$peakGdi = ($ledger.Events | Measure-Object Gdi -Maximum).Maximum
Require ($peakGdi -lt 7000) 'Native GDI resource guard exceeded.'
$pure = Read-Json (Join-Path $taskRoot $PureEvidence)
Require ($pure.passed -eq 48 -and $pure.failed -eq 0 -and $pure.nativeParts -eq 0 -and $pure.nativeOpens -eq 0) 'M12 pure gates failed.'
$regression = Join-Path $taskRoot $RegressionEvidence
$contracts = Read-Json (Join-Path $regression 'artifacts/milestone11/pure-results.json')
$transactions = Read-Json (Join-Path $regression 'artifacts/milestone6/pure-result.json')
$construction = Read-Json (Join-Path $regression 'artifacts/milestone9c/pure-result.json')
$qualification = Read-Json (Join-Path $regression 'artifacts/milestone10c/pure-results.json')
Require ($contracts.passed -eq 76 -and $contracts.failed -eq 0 -and $transactions.Passed -eq 33 -and
    $transactions.Total -eq 33 -and $construction.Passed -eq 22 -and $construction.Failed -eq 0 -and
    $qualification.Status -eq 'PASS' -and $qualification.Tests.Count -eq 28) 'Selected isolated regressions failed.'
$legacy = Read-Json (Join-Path $taskCommon 'legacy-assertion-comparison/comparison.json')
Require ($legacy.identicalOutput -and $legacy.passed -eq 36 -and $legacy.total -eq 39) 'Historical M5 baseline comparison differs.'
$out = Join-Path $taskCommon ('audit/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Path $out | Out-Null
$sourcePaths = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
Require ($LASTEXITCODE -eq 0) 'Cannot enumerate current source.'
$source = @($sourcePaths | ForEach-Object { [ordered]@{path=$_.Replace('\','/');sha256=(Hash-File (Join-Path $taskRoot $_))} })
[ordered]@{schemaVersion='0.3';milestone=12;generatedUtc=[DateTime]::UtcNow.ToString('o');
    acceptedNativeFreezeSha256=(Hash-File (Join-Path $taskAccepted 'native-freeze.json'));files=$source
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'source-manifest.json') -Encoding UTF8
$evidencePaths = @(
    'baseline.json','baseline.json.sha256','native-freeze.json','native-freeze.json.sha256',
    'native-schedule-result.json','worker-create.json','worker-migrate.json','original-hashes.json','native-budget.json',
    'continuation-v2/native-freeze.json','continuation-v2/native-freeze.json.sha256','continuation-v2/native-schedule-result.json',
    'authorized-v3/authorization.json','authorized-v3/native-freeze.json','authorized-v3/native-freeze.json.sha256',
    'authorized-v3/native-schedule-result.json','authorized-v3/worker-migrate.json','authorized-v3/worker-interrupt.json',
    'authorized-v3/worker-recover-edit.json','authorized-v3/worker-readback.json','legacy-assertion-comparison/comparison.json'
)
$evidence = @($evidencePaths | ForEach-Object { [ordered]@{path=('artifacts/milestone12/' + $_);sha256=(Hash-File (Join-Path $taskCommon $_))} })
[ordered]@{schemaVersion='0.3';milestone=12;prd='v0.3-draft-2';status='COMPLETE';nativeSchedule='authorized-v3';
    originalOpenLimit=5;explicitAddedOpenCycles=1;authorizedOpenLimit=6;newParts=1;opens=6;closedDocuments=7;
    ownedDocumentsRemaining=0;peakGdi=$peakGdi;allActiveDocumentsRestored=$true;originals=$originals;
    baselineFilesAudited=$baseline.entries.Count;historicalEvidenceFilesAudited=@($baseline.entries | Where-Object category -EQ 'historical_evidence').Count;
    historicalEvidenceChanges=0;existingProductionSourceChanges=0;allowedSourceChanges=$changes;
    m11ContractsUnchanged=$true;acceptedSourceAndBinariesMatchFreeze=$true;priorFailedSourceSnapshotsVerified=2;
    priorFailedNativeRunsPreserved=2;purePassed=48;pureFailed=0;pureEvidence=$PureEvidence;regressionEvidence=$RegressionEvidence;
    regressionPassCounts=[ordered]@{m11Contracts=76;m6Transactions=33;m9cConstruction=22;stepwiseQualification=28};
    legacyM5Comparison=$legacy;finalRevision=2;nativeDepthMm=10;relations=5;dependencies=6;
    recoveryAuthority='exact journaled revision 1; verified before successful revision 2 publish';
    sourceManifestSha256=(Hash-File (Join-Path $out 'source-manifest.json'));evidence=$evidence;
    nativeCallsThisAudit=0;providerCalls=0;laterMilestonesImplemented=@();
    limitations=@('Qualified v0.2 managed rectangular extrusion, through-hole and linear/rectangular patterns only',
        'No external intake, Save As reconciliation, new construction operations or batch editing',
        'Original five-open budget exceeded only by the explicit one-open authorization',
        'Historical regression failures remain documented; no universal v0.2 test-pass claim')
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $out 'verification.json') -Encoding UTF8
Write-Output "M12 COMPLETE offline audit: $out; $($baseline.entries.Count) baseline files preserved; native cumulative 1 Part / 6 opens / 7 closes."
