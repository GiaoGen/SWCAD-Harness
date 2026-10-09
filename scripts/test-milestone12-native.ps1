[CmdletBinding()]
param([string]$PartTemplate = 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone12'
$taskFreeze = Join-Path $taskOutput 'native-freeze.json'
if (Test-Path -LiteralPath $taskFreeze) { throw 'Formal M12 native freeze already exists. No hidden rerun or budget deletion is permitted.' }
if (-not (Test-Path -LiteralPath $PartTemplate)) { throw 'Frozen native template does not exist.' }
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
$taskDll = Join-Path $taskRoot 'tests/CadHarness.ManagedRecovery.Tests/bin/Release/net8.0-windows/CadHarness.ManagedRecovery.Tests.dll'
if (-not (Test-Path -LiteralPath $taskDll)) { throw 'Build and pass the pure suite before native freeze.' }
$taskFiles = @(& git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
$taskBinaryDir = Split-Path -Parent $taskDll
foreach ($taskFile in Get-ChildItem -LiteralPath $taskBinaryDir -File) {
    $taskFiles += $taskFile.FullName.Substring($taskRoot.Length + 1).Replace('\','/')
}
$taskEntries = @($taskFiles | Sort-Object -Unique | ForEach-Object {
    [ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$taskSchedule = @(
    [ordered]@{step='create';newParts=1;openCycles=1;purpose='create/save/close legacy Part; reopen and migrate a preserved copy'},
    [ordered]@{step='interrupt';newParts=0;openCycles=1;purpose='fresh controller rebinds; depth 8 to 10; failure AfterNativeSave before state publish; live rollback and retained journal'},
    [ordered]@{step='recover-edit';newParts=0;openCycles=2;purpose='fresh controller proves prior depth 8; edit to 10; independent saved reopen before manifest pointer publication'},
    [ordered]@{step='readback';newParts=0;openCycles=1;purpose='fresh final controller reads native depth 10 and five relations; actual configuration-switch refusal; close without save'}
)
[ordered]@{schemaVersion='0.3';milestone=12;frozenUtc=[DateTime]::UtcNow.ToString('o');files=$taskEntries;
    template=[ordered]@{path=[IO.Path]::GetFullPath($PartTemplate);sha256=(Get-FileHash -LiteralPath $PartTemplate -Algorithm SHA256).Hash.ToLowerInvariant()};
    newPartLimit=2;openCycleLimit=5;schedule=$taskSchedule;nativeOracle='100x60 plate; 4 diameter6 through-holes at (+/-30,+/-15); depth8 then10; native definition, exact extents, volume, boundaries, seed/directions, centering/symmetry';
    negatives=@('native document GUID','configuration GUID/name','Save As path','stale persistent reference','state scalar drift','actual native configuration switch','dispatch after interrupted publish');
    providerCalls=0;plannerCallableTools=0;structuredPlanResponses=1;boundedExecuteApprovals=1;executableExtensionAlternatives=0
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskFreeze -Encoding UTF8
(Get-FileHash -LiteralPath $taskFreeze -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($taskFreeze + '.sha256') -Encoding ASCII
$taskRuns = @()
foreach ($taskSlot in $taskSchedule) {
    Write-Output "M12 independent controller: $($taskSlot.step)"
    $taskStarted = [DateTime]::UtcNow
    & $taskDotnet $taskDll $taskRoot --worker $taskSlot.step $PartTemplate 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput ('controller-' + $taskSlot.step + '.log'))
    $taskExit = $LASTEXITCODE
    $taskRuns += [ordered]@{step=$taskSlot.step;exitCode=$taskExit;startedUtc=$taskStarted.ToString('o');endedUtc=[DateTime]::UtcNow.ToString('o')}
    if ($taskExit -ne 0) { break }
}
$taskStatus = if ($taskRuns.Count -eq 4 -and @($taskRuns | Where-Object {$_.exitCode -ne 0}).Count -eq 0) { 'COMPLETE' } else { 'PARTIAL' }
[ordered]@{milestone=12;status=$taskStatus;runs=$taskRuns;allAttemptsPreserved=$true;hiddenReruns=0} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'native-schedule-result.json') -Encoding UTF8
if ($taskStatus -ne 'COMPLETE') { throw 'Frozen native acceptance did not complete; preserve the failed slot and budget evidence.' }
Write-Output 'M12 four cold controller processes completed. Verify final evidence and owned-document cleanup.'
