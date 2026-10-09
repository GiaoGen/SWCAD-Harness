[CmdletBinding()]
param([Parameter(Mandatory)][string]$HistoryA,[Parameter(Mandatory)][string]$HistoryB,[string]$EvidenceNamespace='source-v1')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$common=Join-Path $root 'artifacts/milestone13'
if($EvidenceNamespace -notmatch '^source-v[1-9][0-9]*$'){throw 'Use a new explicitly numbered source namespace.'}
$output=Join-Path $common $EvidenceNamespace
if(Test-Path -LiteralPath $output){throw 'Native namespace already exists; no hidden rerun.'}
$HistoryA=[IO.Path]::GetFullPath($HistoryA);$HistoryB=[IO.Path]::GetFullPath($HistoryB)
if($HistoryA -eq $HistoryB){throw 'Select two distinct external files.'}
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$dll=Join-Path $root 'tests/CadHarness.ExternalObservation.Tests/bin/Release/net8.0-windows/CadHarness.ExternalObservation.Tests.dll'
$paths=@(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if($LASTEXITCODE -ne 0){throw 'Cannot enumerate source freeze.'}
$paths+=@(Get-ChildItem -LiteralPath (Split-Path -Parent $dll) -File | ForEach-Object {$_.FullName.Substring($root.Length+1).Replace('\','/')})
$files=@($paths | Sort-Object -Unique | ForEach-Object {[ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})
$originals=@(@($HistoryA,$HistoryB) | ForEach-Object {[ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant();SizeBytes=(Get-Item -LiteralPath $_).Length}})
New-Item -ItemType Directory -Path $output | Out-Null
$originals | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'originals.json') -Encoding UTF8
[ordered]@{milestone=13;prd='v0.3-draft-2';utc=[DateTime]::UtcNow.ToString('o');files=$files;originals=$originals;
    schedule=@('history-a','history-b','reopen','overflow','configuration-if-present');newParts=0;maximumCumulativeOpens=5;
    oracle='Independent FeatureManager.GetFeatures(false), native type/suppression, definition depth/pattern scalars and cylindrical diameter; immutable originals and copies; stable reference IDs on fresh-controller reopen';
    missingFixturePolicy='Never create/suppress/switch/rebuild/save fixture to fabricate evidence. Missing native cases make M13 PARTIAL.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'native-freeze.json') -Encoding UTF8
(Get-FileHash -LiteralPath (Join-Path $output 'native-freeze.json') -Algorithm SHA256).Hash.ToLowerInvariant() |
    Set-Content -LiteralPath (Join-Path $output 'native-freeze.json.sha256') -Encoding ASCII
$runs=@()
function Run-Slot([string]$slot,[string]$part,[string]$config='') {
    Write-Output "M13 frozen controller: $slot"
    if($config){& $dotnet $dll $root --native $EvidenceNamespace $slot $part $config 2>&1 | Tee-Object -FilePath (Join-Path $output ($slot+'.log'))}
    else{& $dotnet $dll $root --native $EvidenceNamespace $slot $part 2>&1 | Tee-Object -FilePath (Join-Path $output ($slot+'.log'))}
    $code=$LASTEXITCODE
    $script:runs+=[ordered]@{slot=$slot;exitCode=$code}
    return $code
}
# Run-Slot emits diagnostic output as well as the exit code; use its final item.
$failed=$false
foreach($slot in @('history-a','history-b','reopen','overflow')) {
    $part=if($slot -eq 'history-b'){$HistoryB}else{$HistoryA}
    $result=@(Run-Slot $slot $part)
    $result | Select-Object -SkipLast 1 | Write-Output
    if($result[-1] -ne 0){$failed=$true;break}
}
$configurationSkipped=$true
if(-not $failed){
    $b=Get-Content -LiteralPath (Join-Path $output 'history-b-result.json') -Raw | ConvertFrom-Json
    $other=@($b.inspection.Configurations | Where-Object {$_ -cne $b.inspection.Observation.Model.Selection.ConfigurationName} | Sort-Object | Select-Object -First 1)
    if($other.Count -eq 1){
        $configurationSkipped=$false
        $result=@(Run-Slot 'configuration' $HistoryB $other[0]);$result | Select-Object -SkipLast 1 | Write-Output
        if($result[-1] -ne 0){$failed=$true}
    }
}
[ordered]@{milestone=13;executionStatus=$(if($failed){'PARTIAL'}else{'PASS'});runs=$runs;configurationSkipped=$configurationSkipped;
    completionStatus='PENDING_FINAL_FIXTURE_COVERAGE_AUDIT';allAttemptsPreserved=$true;budgetLedger='artifacts/milestone13/native-budget.json'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'schedule-result.json') -Encoding UTF8
if($failed){throw 'M13 native schedule failed; stop and preserve reports/ledger. No implicit rerun.'}
Write-Output 'M13 schedule executed; audit native fixture coverage before claiming completion.'
