[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$common=Join-Path $root 'artifacts/milestone13'
& (Join-Path $PSScriptRoot 'verify-milestone13.ps1')
$auditFile=Get-ChildItem -LiteralPath (Join-Path $common 'audit') -Filter 'verification.json' -File -Recurse |
    Sort-Object FullName | Select-Object -Last 1
$audit=Get-Content -LiteralPath $auditFile.FullName -Raw | ConvertFrom-Json
if($audit.status -ne 'COMPLETE' -or $audit.opens -ne 6 -or $audit.closes -ne 6 -or
    $audit.newParts -ne 0 -or $audit.missingNativeCriteria.Count -ne 0){throw 'Full M13 coverage/authorized lifecycle audit did not pass.'}
$output=Join-Path $common 'authorized-v2'
$first=Get-Content -LiteralPath (Join-Path $output 'supplement-first-overlay.json') -Raw | ConvertFrom-Json
$second=Get-Content -LiteralPath (Join-Path $output 'supplement-second-overlay.json') -Raw | ConvertFrom-Json
$schedule=Get-Content -LiteralPath (Join-Path $output 'native-freeze.json') -Raw | ConvertFrom-Json
if($first.selection.configurationName -cne $schedule.schedule[0].configuration -or
    $second.selection.configurationName -cne $schedule.schedule[1].configuration){throw 'Native configurations differ from the user-specified frozen selections.'}
$suppressed=@($first.features | Where-Object nativeType -EQ 'LPattern')
$active=@($second.features | Where-Object nativeType -EQ 'LPattern')
if($suppressed.Count -ne 1 -or $active.Count -ne 1){throw 'Fixture has no unambiguous single native linear-pattern inventory row per configuration.'}
if($suppressed[0].health -ne 'suppressed' -or $suppressed[0].editSupport -ne 'read_only' -or
    $suppressed[0].parameters.Count -ne 0 -or $suppressed[0].geometry.Count -ne 0){throw 'Suppressed feature gained qualification or was omitted.'}
if($active[0].health -ne 'healthy' -or $active[0].editSupport -ne 'read_only' -or
    $null -eq $active[0].nativeReference){throw 'Active pattern is not a verified read-only native observation.'}
$count=@($active[0].parameters | Where-Object key -EQ 'pattern_count')
$spacing=@($active[0].parameters | Where-Object key -EQ 'pattern_spacing')
if($count.Count -ne 1 -or $spacing.Count -ne 1 -or $count[0].value -ne 2 -or $count[0].unit -ne 'count' -or
    [Math]::Abs($spacing[0].value-40) -ge 0.001 -or $spacing[0].unit -ne 'millimeter'){throw 'Final active direction-1 scalar/provenance oracle differs.'}
if($first.selection.documentId -cne $second.selection.documentId -or
    $first.selection.configurationId -ceq $second.selection.configurationId -or
    $first.selection.source.sha256 -cne $second.selection.source.sha256){throw 'Native configuration transition lost selected source lineage.'}
$target=Join-Path $auditFile.DirectoryName 'completion.json'
if(Test-Path -LiteralPath $target){throw 'Never overwrite an earlier completion record.'}
[ordered]@{
    milestone=13;status='COMPLETE';audit=$auditFile.FullName;
    auditSha256=(Get-FileHash -LiteralPath $auditFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant();
    sourceManifestSha256=$audit.sourceManifestSha256;utc=[DateTime]::UtcNow.ToString('o');
    configurations=@($first.selection.configurationName,$second.selection.configurationName);
    suppressedPattern=[ordered]@{health=$suppressed[0].health;support=$suppressed[0].editSupport;
        bindingReferenceAvailable=($null -ne $suppressed[0].nativeReference);parameters=0;geometry=0};
    activePattern=[ordered]@{health=$active[0].health;support=$active[0].editSupport;subtype=$active[0].subtype;
        direction1Count=$count[0].value;direction1SpacingMm=$spacing[0].value};
    crossSuppressionBindingRemapClaimed=$false;
    original=$first.selection.source;allOriginalsPreserved=$true;newParts=0;opens=6;closes=6;
    originalOpenLimit=5;explicitAddedOpenCycles=1;authorizedOpenLimit=6;
    nativeCallsThisVerification=0;allPriorPartialReportsPreserved=$true;productionChangedAfterNativeFreeze=$false;
    finalReport='docs/milestone-13-completion.md';laterMilestonesImplemented=@()
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $target -Encoding UTF8
Write-Output "M13 COMPLETE final verification: $target; suppressed/active configuration assertions passed; no additional native opens."
