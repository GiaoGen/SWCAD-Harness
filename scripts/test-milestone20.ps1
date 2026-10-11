[CmdletBinding()]
param([ValidateSet('Prepare','Execute','Summarize')][string]$Mode='Prepare',
      [string]$RunId='acceptance_v1',
      [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist',
      [string]$PartTemplate='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$output=Join-Path $root "artifacts/milestone20/$RunId"
$bin=Join-Path $root "artifacts/milestone20/frozen-bin-$RunId"
$runner=Join-Path $bin 'CadHarness.CoreAcceptance.Tests.dll'
$env:CAD_HARNESS_PART_TEMPLATE=$PartTemplate
function Worker([string]$Phase,[string]$Slot) {
    & $sdk $runner $root $Phase $RunId $Slot 2>&1 | Tee-Object -FilePath (Join-Path $output "slots/$Slot/$($Phase.TrimStart('-')).log")
    if($LASTEXITCODE -ne 0){throw "Controller/resource failure in $Slot; stop and preserve scene. No automatic retry."}
}
if($Mode -eq 'Prepare') {
    if(Test-Path -LiteralPath $output){throw 'Frozen namespace already exists'}
    & $sdk build (Join-Path $root 'tests/CadHarness.CoreAcceptance.Tests/CadHarness.CoreAcceptance.Tests.csproj') -c Release --disable-build-servers '-p:NuGetAudit=false' "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/"
    if($LASTEXITCODE -ne 0){throw 'M20 build failed'}
    & $sdk $runner $root --pure
    if($LASTEXITCODE -ne 0){throw 'M20 frozen-task pure checks failed'}
    & $sdk $runner $root --freeze $RunId
    if($LASTEXITCODE -ne 0){throw 'M20 freeze failed'}
    return
}
$freeze=Get-Content -LiteralPath (Join-Path $output 'freeze.json') -Raw | ConvertFrom-Json
if($Mode -eq 'Execute') {
    if(Test-Path -LiteralPath (Join-Path $output 'execution-started.json')){throw 'Formal run cannot be repeated automatically'}
    [ordered]@{utc=[DateTime]::UtcNow.ToString('o');noRepair=$true;noRetry=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'execution-started.json') -Encoding utf8
    foreach($slot in $freeze.slots) {
        New-Item -ItemType Directory -Path (Join-Path $output "slots/$($slot.id)") -Force | Out-Null
        if($slot.mode -in @('real','negative-planning')){Worker '--plan' $slot.id}
        if($slot.mode -ne 'negative-planning') {
            Worker '--native' $slot.id
            if(Test-Path -LiteralPath (Join-Path $output "slots/$($slot.id)/saved-file.json")){Worker '--cold' $slot.id}
        }
    }
}
$rows=@()
foreach($slot in $freeze.slots) {
    $dir=Join-Path $output "slots/$($slot.id)"
    $p=$n=$c=$null
    if(Test-Path -LiteralPath (Join-Path $dir 'planning-result.json')){$p=Get-Content -LiteralPath (Join-Path $dir 'planning-result.json') -Raw | ConvertFrom-Json}
    if(Test-Path -LiteralPath (Join-Path $dir 'native-result.json')){$n=Get-Content -LiteralPath (Join-Path $dir 'native-result.json') -Raw | ConvertFrom-Json}
    if(Test-Path -LiteralPath (Join-Path $dir 'cold-result.json')){$c=Get-Content -LiteralPath (Join-Path $dir 'cold-result.json') -Raw | ConvertFrom-Json}
    $full=$n.nativePassed -and $n.editablePassed -and $n.followUpPassed -and $n.cleanup -and $c.nativePassed -and $c.reopenPassed -and $c.editablePassed -and $c.cleanup
    $rows += [ordered]@{id=$slot.id;task=$slot.task;mode=$slot.mode;source=$slot.source;planning=$p;native=$n;cold=$c;completeTask=[bool]$full}
}
$gate=@()
foreach($family in @('C1','C2','C3','C4')) {
    $real=@($rows | Where-Object {$_.task -eq $family -and $_.mode -eq 'real'})
    $gate += [ordered]@{family=$family;scheduled=3;recorded=@($real | Where-Object {$null -ne $_.planning}).Count;complete=@($real | Where-Object {$_.completeTask}).Count;required=2}
}
$nativeLedger=Get-Content -LiteralPath (Join-Path $root 'artifacts/milestone20/native-budget.json') -Raw | ConvertFrom-Json
$summary=[ordered]@{baseline=$freeze.baseline;run=$RunId;status='PARTIAL';criteriaUnchanged=$true;deterministicRequired=4;deterministicComplete=@($rows | Where-Object {$_.mode -eq 'deterministic' -and $_.task -in @('C1','C2','C3','C4') -and $_.completeTask}).Count;
    realGate=$gate;realCompleteTotal=@($rows | Where-Object {$_.mode -eq 'real' -and $_.completeTask}).Count;realRequiredTotal=8;rows=$rows;ledger=$nativeLedger;deferred=$freeze.deferred;
    timingPolicy='Attempt-level milliseconds retained for every slot; successful-only complete-task timing unavailable when no complete tasks. No efficiency comparison.'}
$summary | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8 -NoClobber
Write-Output "M20 measured summary: $output/summary.json. Thresholds are not adjusted for unsupported cases."
