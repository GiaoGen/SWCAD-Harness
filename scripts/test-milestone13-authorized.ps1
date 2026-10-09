[CmdletBinding()]
param(
    [switch]$UserAuthorizedAdditionalOpen,
    [string]$Fixture,
    [string]$ConfigurationA,
    [string]$ConfigurationB
)
$ErrorActionPreference='Stop'
if(-not $UserAuthorizedAdditionalOpen){throw 'Explicit human authorization for one additional open is required.'}
$root=Split-Path -Parent $PSScriptRoot
$common=Join-Path $root 'artifacts/milestone13'
$output=Join-Path $common 'authorized-v2'
$grant=Join-Path $output 'authorization.json'
$ledgerPath=Join-Path $common 'native-budget.json'
if(-not (Test-Path -LiteralPath $grant)){
    $ledger=Get-Content -LiteralPath $ledgerPath -Raw | ConvertFrom-Json
    if($ledger.Opens -ne 4 -or $ledger.Closes -ne 4 -or $ledger.NewParts -ne 0 -or $ledger.MaximumOpenCycles -ne 5){throw 'Ledger differs from the one-open authorization context.'}
    New-Item -ItemType Directory -Path $output | Out-Null
    Copy-Item -LiteralPath $ledgerPath -Destination (Join-Path $output 'ledger-before-authorization.json')
    [ordered]@{milestone=13;userAuthorized=$true;addedOpenCycles=1;oldOpenLimit=5;newOpenLimit=6;newPartLimit=0;
        priorOpens=4;priorCloses=4;utc=[DateTime]::UtcNow.ToString('o');evidenceNamespace='authorized-v2';
        authorization='Human explicitly replied to authorize one additional native open; fixture path still required';
        originalLedgerSha256=(Get-FileHash -LiteralPath $ledgerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $grant -Encoding UTF8
    (Get-FileHash -LiteralPath $grant -Algorithm SHA256).Hash.ToLowerInvariant() |
        Set-Content -LiteralPath ($grant+'.sha256') -Encoding ASCII
    $ledger.MaximumOpenCycles=6
    $ledger.Events+=@([ordered]@{Kind='explicit-budget-authorization';Path=$grant;Title='authorized-v2';ControllerPid=$PID;Utc=[DateTime]::UtcNow.ToString('o');Gdi=0})
    $temp=$ledgerPath+'.authorization.tmp'
    $ledger | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temp -Encoding UTF8
    [IO.File]::Move($temp,$ledgerPath,$true)
}
if((Get-FileHash -LiteralPath $grant -Algorithm SHA256).Hash.ToLowerInvariant() -cne (Get-Content -LiteralPath ($grant+'.sha256') -Raw).Trim()){throw 'Recorded grant drifted.'}
$ledger=Get-Content -LiteralPath $ledgerPath -Raw | ConvertFrom-Json
if($ledger.MaximumOpenCycles -eq 5){
    $record=Get-Content -LiteralPath $grant -Raw | ConvertFrom-Json
    $temp=$ledgerPath+'.authorization.tmp'
    if((Get-FileHash -LiteralPath $ledgerPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.originalLedgerSha256 -or -not (Test-Path -LiteralPath $temp)){throw 'Cannot reconcile incomplete grant recording.'}
    $pending=Get-Content -LiteralPath $temp -Raw | ConvertFrom-Json
    if($pending.Opens -ne 4 -or $pending.Closes -ne 4 -or $pending.MaximumOpenCycles -ne 6){throw 'Pending grant does not match authorization.'}
    [IO.File]::Move($temp,$ledgerPath,$true)
}
if(-not $Fixture){Write-Output 'One additional open authorized (cumulative limit 6); no native call. Waiting for a pre-existing suppressed/multi-configuration fixture.';return}
if($ConfigurationA -and $ConfigurationB -and $ConfigurationA -ceq $ConfigurationB){throw 'Select distinct native configurations.'}
if(Test-Path -LiteralPath (Join-Path $output 'native-freeze.json')){throw 'Supplemental namespace already attempted; never rerun.'}
$Fixture=[IO.Path]::GetFullPath($Fixture)
$ledger=Get-Content -LiteralPath $ledgerPath -Raw | ConvertFrom-Json
if($ledger.Opens -ne 4 -or $ledger.Closes -ne 4 -or $ledger.MaximumOpenCycles -ne 6){throw 'Two supplemental opens no longer fit the cumulative authorized ledger.'}
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$dll=Join-Path $root 'tests/CadHarness.ExternalObservation.Tests/bin/Release/net8.0-windows/CadHarness.ExternalObservation.Tests.dll'
$paths=@(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root ls-files --cached --others --exclude-standard | Sort-Object -Unique)
$paths+=@(Get-ChildItem -LiteralPath (Split-Path -Parent $dll) -File | ForEach-Object {$_.FullName.Substring($root.Length+1).Replace('\','/')})
$paths+=@('artifacts/milestone13/authorized-v2/authorization.json','artifacts/milestone13/authorized-v2/authorization.json.sha256')
$files=@($paths | Sort-Object -Unique | ForEach-Object {[ordered]@{path=$_.Replace('\','/');sha256=(Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})
$originals=@((Get-Content -LiteralPath (Join-Path $common 'source-v1/originals.json') -Raw | ConvertFrom-Json))
$originals+=@([ordered]@{Path=$Fixture;Sha256=(Get-FileHash -LiteralPath $Fixture -Algorithm SHA256).Hash.ToLowerInvariant();SizeBytes=(Get-Item -LiteralPath $Fixture).Length})
[ordered]@{milestone=13;files=$files;originals=$originals;utc=[DateTime]::UtcNow.ToString('o');newParts=0;maximumCumulativeOpens=6;
    schedule=@([ordered]@{slot='supplement-first';configuration=$(if($ConfigurationA){$ConfigurationA}else{'saved active configuration'})},
        [ordered]@{slot='supplement-second';configuration=$(if($ConfigurationB){$ConfigurationB}else{'first actual alternative configuration from first native report; skip if absent'})});
    missingFixturePolicy='No mutation to create a suppressed feature or configuration; coverage audited from actual native evidence.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'native-freeze.json') -Encoding UTF8
(Get-FileHash -LiteralPath (Join-Path $output 'native-freeze.json') -Algorithm SHA256).Hash.ToLowerInvariant() |
    Set-Content -LiteralPath (Join-Path $output 'native-freeze.json.sha256') -Encoding ASCII
$runs=@();$configurationSkipped=$false
foreach($item in @(@{slot='supplement-first';config=$ConfigurationA},@{slot='supplement-second';config=$ConfigurationB})){
    if($item.slot -eq 'supplement-second' -and -not $item.config){
        $first=Get-Content -LiteralPath (Join-Path $output 'supplement-first-result.json') -Raw | ConvertFrom-Json
        $alternatives=@($first.inspection.Configurations | Where-Object {$_ -cne $first.inspection.Observation.Model.Selection.ConfigurationName} | Sort-Object | Select-Object -First 1)
        if($alternatives.Count -eq 0){$configurationSkipped=$true;break}
        $item.config=$alternatives[0]
    }
    if($item.config){& $dotnet $dll $root --native 'authorized-v2' $item.slot $Fixture $item.config 2>&1 | Tee-Object -FilePath (Join-Path $output ($item.slot+'.log'))}
    else{& $dotnet $dll $root --native 'authorized-v2' $item.slot $Fixture 2>&1 | Tee-Object -FilePath (Join-Path $output ($item.slot+'.log'))}
    $code=$LASTEXITCODE;$runs+=@([ordered]@{slot=$item.slot;exitCode=$code})
    if($code -ne 0){break}
}
[ordered]@{runs=$runs;executionStatus=$(if(@($runs | Where-Object exitCode -NE 0).Count -eq 0){'PASS'}else{'PARTIAL'});
    configurationSkipped=$configurationSkipped;
    completionStatus='PENDING_FINAL_FIXTURE_COVERAGE_AUDIT';allAttemptsPreserved=$true
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'schedule-result.json') -Encoding UTF8
if(@($runs | Where-Object exitCode -NE 0).Count -ne 0){throw 'Supplement failed; retain all evidence and stop.'}
Write-Output 'Authorized supplemental schedule executed; no completion claim until native coverage audit.'
