[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$AuditId)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$factory=Join-Path $root 'artifacts/fixture-factory'
$output=Join-Path $factory "audits/$AuditId"
if(Test-Path -LiteralPath $output){throw 'Audit evidence already exists; never overwrite.'}
function Identity([string]$Path){
    $full=[IO.Path]::GetFullPath($Path)
    $s=[IO.File]::Open('\\?\'+$full,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try{[ordered]@{path=$full;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant();bytes=$s.Length}}finally{$s.Dispose()}
}
function Verify($Expected){$actual=Identity $Expected.path;if($actual.sha256 -ne $Expected.sha256 -or $actual.bytes -ne $Expected.bytes){throw "Evidence drift: $($Expected.path)"}}
function NewJson([string]$Path,$Value){
    $data=[Text.Encoding]::UTF8.GetBytes(($Value|ConvertTo-Json -Depth 24))
    $f=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$f.Write($data);$f.Flush($true)}finally{$f.Dispose()}
}
$baselinePath=Join-Path $factory 'baseline.json'
$baselineId=Identity $baselinePath
if($baselineId.sha256 -ne (Get-Content -LiteralPath ($baselinePath+'.sha256') -Raw).Trim()){throw 'Historical Factory baseline changed.'}
$baseline=Get-Content -LiteralPath $baselinePath -Raw|ConvertFrom-Json
$changes=@()
foreach($entry in $baseline.entries){
    $actual=Identity (Join-Path $root $entry.path)
    if($actual.sha256 -ne $entry.sha256){$changes+=$entry.path}
}
if($changes.Count){throw "Production/historical evidence changed: $($changes -join ', ')"}
$m14Path=Join-Path $root 'artifacts/milestone14/native-budget.json'
$m14Identity=Identity $m14Path
if($m14Identity.sha256 -ne '7dec769284ff2bb78916a5b4d66bed4d8925d78107570e0f0937a9328c511db8'){throw 'M14 ledger byte identity changed.'}
$m14=Get-Content -LiteralPath $m14Path -Raw|ConvertFrom-Json
if($m14.OpenAttempts -ne 2 -or $m14.OwnedTitles.Count -ne 0){throw 'M14 was unexpectedly used.'}
$budgetPath=Join-Path $factory 'preparation-budget.json'
$budget=Get-Content -LiteralPath $budgetPath -Raw|ConvertFrom-Json
if($budget.ownedTitles.Count -ne 0 -or $budget.creationAttempts -gt 12 -or $budget.openAttempts -gt 16){throw 'Preparation ownership/budget audit failed.'}
foreach($pair in @(@('reserve-creation','creationAttempts'),@('reserve-open','openAttempts'),@('closed-owned','closedDocuments'))){
    if(@($budget.events|Where-Object kind -eq $pair[0]).Count -ne $budget.($pair[1])){throw 'Lifecycle counters do not reconcile.'}
}
if($budget.closedDocuments -ne $budget.creationAttempts+$budget.openAttempts){throw 'Created/opened fixtures did not all close.'}
$exits=@($budget.events|Where-Object kind -eq 'controller-exit')
if($exits.Count -ne @($budget.events|Where-Object kind -eq 'controller-start').Count -or @($exits|Where-Object detail -ne 'True').Count){throw 'Controller/active restoration audit failed.'}
$selected=@('prep-v4/packages/dev_core/ready.json','prep-v4/packages/dev_origin/ready.json','prep-v4/packages/dev_unknown_descendant/ready.json','prep-v8/packages/dev_equation_driver/ready.json')
$registry=@()
foreach($relative in $selected){
    $readyPath=Join-Path (Join-Path $factory 'runs') $relative
    $ready=Get-Content -LiteralPath $readyPath -Raw|ConvertFrom-Json
    if($ready.dataset -ne 'development' -or $ready.status -ne 'PREPARATION_SELF_CHECKED_NOT_M14_ACCEPTANCE'){throw 'Ready scope mismatch.'}
    foreach($identity in $ready.manifest,$ready.proof,$ready.nativePart,$ready.readerFreeze){Verify $identity}
    $manifest=Get-Content -LiteralPath $ready.manifest.path -Raw|ConvertFrom-Json
    $proof=Get-Content -LiteralPath $ready.proof.path -Raw|ConvertFrom-Json
    if(!$proof.passed -or !$proof.activeDocumentRestored -or $proof.fixtureId -ne $manifest.fixtureId){throw 'Independent proof is not passed/closed/restored.'}
    foreach($freezeIdentity in $ready.readerFreeze,$manifest.sourceFreeze){
        $freeze=Get-Content -LiteralPath $freezeIdentity.path -Raw|ConvertFrom-Json
        Verify $freeze.baseline;foreach($item in $freeze.files){Verify $item}
    }
    if($manifest.fixtureId -eq 'dev_equation_driver' -and !@($proof.checks|Where-Object {$_ -like 'Active non-global equation targets native host depth:*'}).Count){throw 'Equation source has only the superseded weak proof.'}
    $registry+=[ordered]@{fixtureId=$manifest.fixtureId;dataset='development';status='PREPARATION_SELF_CHECKED_NOT_M14_ACCEPTANCE';ready=(Identity $readyPath);manifest=$ready.manifest;proof=$ready.proof;nativePart=$ready.nativePart;configuration=$manifest.configuration;expected=$manifest.expected}
}
New-Item -ItemType Directory -Path $output -Force|Out-Null
NewJson (Join-Path $output 'fixture-registry.json') ([ordered]@{schemaVersion='1.0';m14AcceptanceRun=$false;heldOutGenerated=$false;fixtures=$registry})
$outcomes=@()
foreach($run in Get-ChildItem -LiteralPath (Join-Path $factory 'runs') -Directory){
    $schedule=Get-Content -LiteralPath (Join-Path $run.FullName 'preparation-schedule.json') -Raw|ConvertFrom-Json
    foreach($slot in $schedule.slots){
        $prefix=$run.Name+':'+$slot.fixtureId+':'
        $attempts=@($budget.attemptSlots|Where-Object {$_.StartsWith($prefix)})
        $package=Join-Path $run.FullName ('packages/'+$slot.fixtureId)
        $status='NOT_EXECUTED_NO_NATIVE_CALL'
        if(Test-Path -LiteralPath (Join-Path $package 'build-failure.json')){$status='BUILD_FAILED_EVIDENCE_RETAINED'}
        elseif(Test-Path -LiteralPath (Join-Path $package 'reader-proof.json')){
            $p=Get-Content -LiteralPath (Join-Path $package 'reader-proof.json') -Raw|ConvertFrom-Json
            $readyFile=Join-Path $package 'ready.json'
            if(!$p.passed){$status='READER_FAILED_EVIDENCE_RETAINED'}
            elseif(@($registry|Where-Object {$_.ready.path -eq $readyFile}).Count){$status='SELECTED_SELF_CHECKED_DEVELOPMENT_SOURCE'}
            else{$status='HISTORICAL_PASS_SUPERSEDED_NOT_SELECTED'}
        }
        $outcomes+=[ordered]@{run=$run.Name;fixtureId=$slot.fixtureId;status=$status;attemptSlots=$attempts;formalAcceptance=$false}
    }
}
NewJson (Join-Path $output 'all-slot-outcomes.json') $outcomes
$a1='D:\document\A1.SLDPRT'
$a1Identity=if(Test-Path -LiteralPath $a1){Identity $a1}else{$null}
$summary=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');status='FOUR_DEVELOPMENT_FIXTURES_READY_NOT_M14_ACCEPTANCE';baseline=$baselineId;unchangedBaselineEntries=@($baseline.entries).Count;historicalChanges=$changes;factoryLedger=(Identity $budgetPath);creationAttempts=$budget.creationAttempts;openAttempts=$budget.openAttempts;closedDocuments=$budget.closedDocuments;maximumCreationAttempts=12;maximumOpenAttempts=16;ownedTitles=$budget.ownedTitles;activeRestorationVerifiedControllers=$exits.Count;maximumRecordedGdi=($budget.events|Measure-Object gdi -Maximum).Maximum;m14Ledger=$m14Identity;m14OpenAttempts=$m14.OpenAttempts;m14MaximumCumulativeOpens=12;m14AcceptanceRun=$false;heldOutGenerated=$false;a1CurrentFingerprint=$a1Identity;a1CompatibilityClaim='UNCHANGED_UNVERIFIED_BY_FACTORY';registry=(Identity (Join-Path $output 'fixture-registry.json'));slotOutcomes=(Identity (Join-Path $output 'all-slot-outcomes.json'))}
NewJson (Join-Path $output 'audit.json') $summary
$summary|ConvertTo-Json -Depth 5
