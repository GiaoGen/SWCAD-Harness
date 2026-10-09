[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$common=Join-Path $root 'artifacts/milestone13'
function Hash-File([string]$path){
    $s=[IO.FileStream]::new('\\?\'+[IO.Path]::GetFullPath($path),[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}
}
function Read-Json([string]$path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function Require([bool]$condition,[string]$message){if(-not $condition){throw $message}}
function Freeze([string]$path){Require ((Hash-File $path) -ceq (Get-Content -LiteralPath ($path+'.sha256') -Raw).Trim()) "Freeze drift: $path";Read-Json $path}
$baseline=Freeze (Join-Path $common 'baseline.json')
$changes=@()
foreach($entry in $baseline.entries){
    $actual=Hash-File (Join-Path $root $entry.path)
    if($actual -cne $entry.sha256){$changes+=@([ordered]@{path=$entry.path;category=$entry.category;before=$entry.sha256;after=$actual})}
}
Require (@($changes|Where-Object {$_.path -ne 'CadHarness.sln' -or $_.category -ne 'source'}).Count -eq 0) 'Pre-existing source or historical evidence changed.'
$runs=@('source-v1')
if(Test-Path -LiteralPath (Join-Path $common 'authorized-v2/native-freeze.json')){$runs+=@('authorized-v2')}
$models=@{};$reports=@();$originals=@();$evidence=@()
foreach($run in $runs){
    $dir=Join-Path $common $run
    $freeze=Freeze (Join-Path $dir 'native-freeze.json')
    foreach($entry in $freeze.files){Require ((Hash-File (Join-Path $root $entry.path)) -ceq $entry.sha256) "Accepted source/binary drift: $($entry.path)"}
    foreach($original in $freeze.originals){
        Require ((Hash-File $original.Path) -ceq $original.Sha256 -and (Get-Item -LiteralPath $original.Path).Length -eq $original.SizeBytes) 'Engineer original bytes drifted.'
        if($originals.Path -notcontains $original.Path){$originals+=@($original)}
    }
    foreach($file in Get-ChildItem -LiteralPath $dir -File|Where-Object {$_.Name -like '*-result.json' -and $_.Name -ne 'schedule-result.json'}){
        $report=Read-Json $file.FullName
        Require ($report.status -eq 'COMPLETE' -and $report.oracleVerified -and $report.inspection.ReadOnlyNative -and
            $report.inspection.OriginalPreserved -and $report.inspection.CopyPreserved -and
            $report.inspection.OriginalActiveRestored -and $report.inspection.NoOwnedDocumentRemains -and
            $report.inspection.DirtyOnOpen -eq $report.inspection.DirtyAfterInspection) "Native worker failed: $($file.Name)"
        $copy=$report.inspection.Observation.Model.Selection.WorkingCopy
        Require ((Hash-File $copy.Path) -ceq $copy.Sha256) 'Saved inspection copy drifted.'
        $model=Read-Json (Join-Path $dir ($report.slot+'-overlay.json'))
        Require (@($model.features|Where-Object editSupport -NE 'read_only').Count -eq 0) 'M13 exposed mutation.'
        $models[$report.slot]=$model;$reports+=@($report)
        $evidence+=@([ordered]@{path=$file.FullName.Substring($root.Length+1).Replace('\','/');sha256=(Hash-File $file.FullName)})
    }
}
foreach($slot in @('history-a','history-b','reopen','overflow')){Require ($models.ContainsKey($slot)) "Required native slot missing: $slot"}
$a=$models['history-a'];$b=$models['history-b'];$reopen=$models['reopen'];$overflow=$models['overflow']
Require ($a.inventoryComplete -and $b.inventoryComplete -and $reopen.inventoryComplete) 'Normal observations incomplete.'
Require (-not $overflow.inventoryComplete -and $overflow.limitOutcome -eq 'OBSERVATION_LIMIT_EXCEEDED' -and $overflow.features.Count -eq 1) 'Overflow was not partial/noneditable.'
Require ($a.selection.source.sha256 -ceq $reopen.selection.source.sha256 -and $a.selection.configurationId -ceq $reopen.selection.configurationId -and
    (($a.features.semanticId -join ',') -ceq ($reopen.features.semanticId -join ','))) 'Unchanged cold reopen identity is unstable.'
Require (@($reports.controllerPid|Sort-Object -Unique).Count -eq $reports.Count) 'Native schedule reused a controller process.'
foreach($report in $reports|Where-Object slot -NE 'overflow'){
    Require ($report.inspection.Observation.Inventory.InventoryCount -eq $report.nativeArrayCount -and
        $report.inspection.Observation.Inventory.NativeInventoryCountExact) 'Inventory differs from independent FeatureManager.GetFeatures(false).'
}
$by=@{};foreach($f in $b.features){$by[$f.semanticId]=$f}
$unsupportedDescendants=@($b.dependencies|Where-Object {$by[$_.dependent].nativeType -eq 'ICE' -and $by[$_.prerequisite].nativeType -eq 'Extrusion'})
Require ($unsupportedDescendants.Count -gt 0 -and @($b.features|Where-Object {$_.nativeType -eq 'ICE' -and $_.subtype -ne 'unrecognized'}).Count -eq 0) 'Unsupported downstream feature was not retained as read-only unknown.'
Require (@($a.features|Where-Object nativeType -EQ 'ICE').Count -eq 0 -and @($b.features|Where-Object nativeType -EQ 'ICE').Count -gt 0) 'Two native histories not distinguished.'
$suppressed=@($models.Values|ForEach-Object features|Where-Object health -EQ 'suppressed')
Require (@($suppressed|Where-Object {$_.parameters.Count -gt 0 -or $_.geometry.Count -gt 0}).Count -eq 0) 'Suppressed target was scalar/geometry qualified.'
$configurationVerified=$false
if($models.ContainsKey('supplement-first') -and $models.ContainsKey('supplement-second')){
    $first=$models['supplement-first'];$second=$models['supplement-second']
    Require ($first.selection.source.sha256 -ceq $second.selection.source.sha256 -and
        $first.selection.documentId -ceq $second.selection.documentId -and
        $first.selection.configurationName -cne $second.selection.configurationName -and
        $first.selection.configurationId -cne $second.selection.configurationId) 'Supplemental configuration identity evidence is invalid.'
    $configurationVerified=$true
}
$ledger=Read-Json (Join-Path $common 'native-budget.json')
Require ($ledger.NewParts -eq 0 -and $ledger.Opens -le $ledger.MaximumOpenCycles -and $ledger.Opens -eq $ledger.Closes) 'Budget exceeded or document leaked.'
Require (@($ledger.Events|Where-Object Kind -EQ 'reserve-open').Count -eq $ledger.Opens -and
    @($ledger.Events|Where-Object Kind -EQ 'closed-owned').Count -eq $ledger.Closes) 'Lifecycle events disagree with ledger.'
$authorized=$false
$grant=Join-Path $common 'authorized-v2/authorization.json'
if(Test-Path -LiteralPath $grant){
    $g=Freeze $grant
    Require ($g.userAuthorized -and $g.addedOpenCycles -eq 1 -and $g.newOpenLimit -eq 6 -and $ledger.MaximumOpenCycles -eq 6) 'Explicit additional-open authorization mismatch.'
    Require ((Hash-File (Join-Path $common 'authorized-v2/ledger-before-authorization.json')) -ceq $g.originalLedgerSha256) 'Prior ledger snapshot changed.'
    $authorized=$true
}
else{Require ($ledger.MaximumOpenCycles -eq 5) 'Open limit was silently raised.'}
$peak=($ledger.Events|Measure-Object Gdi -Maximum).Maximum;Require ($peak -lt 7000) 'GDI safety guard exceeded.'
$pureFile=Get-ChildItem -LiteralPath (Join-Path $common 'pure') -File -Recurse|Where-Object Name -EQ 'results.json'|Sort-Object FullName|Select-Object -Last 1
$pure=Read-Json $pureFile.FullName;Require ($pure.passed -eq 27 -and $pure.failed -eq 0) 'Pure gate failed.'
$regressionFile=Get-ChildItem -LiteralPath (Join-Path $common 'regression') -File -Recurse|Where-Object {$_.Name -eq 'result.json' -and $_.DirectoryName -notlike '*\artifacts\*\artifacts\*'}|Sort-Object FullName|Select-Object -Last 1
$regression=Read-Json $regressionFile.FullName;Require ($regression.status -eq 'PASS' -and $regression.m12Pure -eq 48 -and $regression.m11Contracts -eq 76) 'Selected regression failed.'
$missing=@();if($suppressed.Count -eq 0){$missing+=@('No pre-existing suppressed feature in supplied native fixtures; pure tests do not substitute native evidence.')}
if(-not $configurationVerified){$missing+=@('No second native configuration in either supplied file; configuration-change native case remains unverified.')}
$status=if($missing.Count -eq 0){'COMPLETE'}else{'PARTIAL'}
$out=Join-Path $common ('audit/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'));New-Item -ItemType Directory -Path $out|Out-Null
$paths=@(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root ls-files --cached --others --exclude-standard|Sort-Object -Unique)
Require ($LASTEXITCODE -eq 0) 'Cannot enumerate final source.'
$source=@($paths|ForEach-Object {[ordered]@{path=$_.Replace('\','/');sha256=(Hash-File (Join-Path $root $_))}})
[ordered]@{milestone=13;files=$source;utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'source-manifest.json') -Encoding UTF8
[ordered]@{milestone=13;prd='v0.3-draft-2';status=$status;implementation='Read-only external inspection implemented; M14 executable capabilities not widened';
    missingNativeCriteria=$missing;originals=$originals;newParts=0;opens=$ledger.Opens;closes=$ledger.Closes;
    originalOpenLimit=5;authorizedOpenLimit=$ledger.MaximumOpenCycles;explicitAddedOpenCycles=$(if($authorized){1}else{0});
    documentsRemaining=0;allActiveDocumentsRestored=$true;peakGdi=$peak;baselineFilesAudited=$baseline.entries.Count;
    historicalEvidenceChanged=0;existingProductionSourceChanged=0;allowedSourceChanges=$changes;
    sourceAndBinariesMatchNativeFreeze=$true;purePassed=27;pureFailed=0;pureEvidence=$pureFile.FullName;
    regression=$regression;regressionEvidence=$regressionFile.FullName;nativeFixtureCount=$originals.Count;
    fixtureInventoryCounts=@($reports|Select-Object slot,nativeArrayCount,oracleScalars);
    unknownDownstreamNativeEdges=$unsupportedDescendants.Count;suppressedNativeFeatures=$suppressed.Count;
    configurationChangeNativeVerified=$configurationVerified;stableColdReopenIdentities=$true;overflowPartialNoneditable=$true;
    evidence=$evidence;sourceManifestSha256=(Hash-File (Join-Path $out 'source-manifest.json'));nativeCallsThisAudit=0;
    laterMilestonesImplemented=@();providerCalls=0;engineerOriginalsModified=0;fixtureMutations=0
}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $out 'verification.json') -Encoding UTF8
Write-Output "M13 $status audit: $out; $($baseline.entries.Count) preserved baseline files; cumulative native 0 Parts / $($ledger.Opens) opens / $($ledger.Closes) closes."
foreach($reason in $missing){Write-Output "UNVERIFIED: $reason"}
