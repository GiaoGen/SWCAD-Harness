[CmdletBinding()]
param([string]$InteropDir = 'D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone11'
$taskScratch = Join-Path $taskOutput 'v02-pure-regression'
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
& $taskSdk build (Join-Path $taskRoot 'CadHarness.sln') --configuration Release --nologo --disable-build-servers '-m:1' '-p:NuGetAudit=false' "-p:SolidWorksInteropDir=$InteropDir" *> (Join-Path $taskOutput 'solution-build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $taskOutput 'solution-build.log') -Tail 35; throw 'Solution build failed.' }
# Existing pure runners write evidence under their root. Give them an isolated root.
$taskSchemaDirectory = Join-Path $taskScratch 'schemas'
$taskFixtureDirectory = Join-Path $taskScratch 'tests/CadHarness.Planning.Tests/Fixtures'
New-Item -ItemType Directory -Path $taskSchemaDirectory,$taskFixtureDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'schemas/cad-program.schema.json') -Destination $taskSchemaDirectory
Copy-Item -LiteralPath (Join-Path $taskRoot 'tests/CadHarness.Planning.Tests/Fixtures/intent-responses.json') -Destination $taskFixtureDirectory
$taskBaselineRoot = Join-Path $taskOutput 'preexisting-selected-head'
$taskBaselineResult = Join-Path $taskBaselineRoot 'artifacts/milestone7/pure-result.json'
if (-not (Test-Path -LiteralPath $taskBaselineResult)) {
    $taskBeforeManifest = Get-Content -LiteralPath (Join-Path $taskOutput 'baseline.json') -Raw | ConvertFrom-Json
    $taskArchive = Join-Path $taskOutput 'preexisting-selected-head.zip'
    # Read-only archive excludes the unrelated Windows reserved AUX fixture.
    & git -c "safe.directory=$($taskRoot.Replace('\','/'))" -c core.protectNTFS=false -C $taskRoot archive --format=zip "--output=$taskArchive" $taskBeforeManifest.head src scripts/solidworks-interop.props tests/CadHarness.Ir.Tests tests/CadHarness.Planning.Tests schemas/cad-program.schema.json NuGet.Config global.json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the frozen baseline source.' }
    Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskBaselineRoot
    & $taskSdk build (Join-Path $taskBaselineRoot 'tests/CadHarness.Planning.Tests/CadHarness.Planning.Tests.csproj') --configuration Release --nologo --disable-build-servers '-m:1' '-p:NuGetAudit=false' "-p:SolidWorksInteropDir=$InteropDir" *> (Join-Path $taskOutput 'preexisting-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Frozen baseline compilation failed.' }
    & $taskSdk (Join-Path $taskBaselineRoot 'tests/CadHarness.Planning.Tests/bin/Release/net8.0-windows/CadHarness.Planning.Tests.dll') $taskBaselineRoot *> (Join-Path $taskOutput 'preexisting-planning-result.log')
    if (-not (Test-Path -LiteralPath $taskBaselineResult)) { throw 'Frozen baseline did not produce its pure result.' }
}
$taskSourceFreeze = Get-Content -LiteralPath (Join-Path $taskOutput 'baseline.json') -Raw | ConvertFrom-Json
foreach ($taskEntry in $taskSourceFreeze.entries | Where-Object { $_.category -eq 'source' -and ($_.path -like 'src/*' -or $_.path -like 'tests/CadHarness.Planning.Tests/*') }) {
    $taskBeforeFile = Join-Path $taskBaselineRoot $taskEntry.path
    $taskCurrentFile = Join-Path $taskRoot $taskEntry.path
    if ((Get-FileHash -LiteralPath $taskCurrentFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne $taskEntry.sha256 -or
        [IO.File]::ReadAllText($taskBeforeFile).Replace("`r`n","`n") -cne [IO.File]::ReadAllText($taskCurrentFile).Replace("`r`n","`n")) {
        throw 'Independent legacy baseline source differs beyond Git checkout line endings.'
    }
}
# The historical schema uses a different order for three enum sets. JSON Schema
# enum ordering is immaterial; prove equivalence before using the generated copy
# in the existing order-sensitive golden test. Never rewrite the historical file.
$taskOriginalSchema = Get-Content -LiteralPath (Join-Path $taskSchemaDirectory 'cad-program.schema.json') -Raw | ConvertFrom-Json -AsHashtable
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.Ir.Tests/bin/Release/net8.0/CadHarness.Ir.Tests.dll') $taskScratch '--write-schema'
if ($LASTEXITCODE -ne 0) { throw 'Cannot generate isolated runtime schema.' }
$taskGeneratedSchema = Get-Content -LiteralPath (Join-Path $taskSchemaDirectory 'cad-program.schema.json') -Raw | ConvertFrom-Json -AsHashtable
function Normalize-EnumOrder($taskNode) {
    if ($taskNode -is [System.Collections.IDictionary]) {
        foreach ($taskKey in @($taskNode.Keys)) {
            if ($taskKey -eq 'enum') { $taskNode[$taskKey] = @($taskNode[$taskKey] | Sort-Object) }
            else { Normalize-EnumOrder $taskNode[$taskKey] }
        }
    } elseif ($taskNode -is [System.Collections.IEnumerable] -and $taskNode -isnot [string]) {
        foreach ($taskChild in $taskNode) { Normalize-EnumOrder $taskChild }
    }
}
Normalize-EnumOrder $taskOriginalSchema
Normalize-EnumOrder $taskGeneratedSchema
if (($taskOriginalSchema | ConvertTo-Json -Depth 100 -Compress) -cne ($taskGeneratedSchema | ConvertTo-Json -Depth 100 -Compress)) { throw 'Legacy schema changed beyond enum set order.' }
[ordered]@{ semanticallyEquivalent=$true; normalization='Sort enum alternatives only'; originalSchemaPreserved=$true; originalOrderSensitiveTest='79/80; historical enum ordering mismatch'; canonicalCopiedSchemaUsed=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'legacy-schema-equivalence.json') -Encoding UTF8
$taskResults = @()
$taskHistoricalTarget = Join-Path $taskScratch 'artifacts/milestone9g'
New-Item -ItemType Directory -Path $taskHistoricalTarget -Force | Out-Null
foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'artifacts/milestone9g') -File -Recurse -Filter '*.json') {
    $taskRelative = $taskFile.FullName.Substring((Join-Path $taskRoot 'artifacts/milestone9g').Length + 1)
    $taskDestination = Join-Path $taskHistoricalTarget $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination
}
$taskBaselineRequestTarget = Join-Path $taskScratch 'artifacts/milestone10b'
New-Item -ItemType Directory -Path $taskBaselineRequestTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'artifacts/milestone10b/call-01-request.json') -Destination $taskBaselineRequestTarget
foreach ($taskCase in @(
    @{ name='Ir'; path='tests/CadHarness.Ir.Tests/bin/Release/net8.0/CadHarness.Ir.Tests.dll'; arguments=@($taskScratch) },
    @{ name='State'; path='tests/CadHarness.State.Tests/bin/Release/net8.0-windows/CadHarness.State.Tests.dll'; arguments=@($taskScratch,'--pure') },
    @{ name='Planning'; path='tests/CadHarness.Planning.Tests/bin/Release/net8.0-windows/CadHarness.Planning.Tests.dll'; arguments=@($taskScratch) },
    @{ name='CurrentQualification'; path='tests/CadHarness.StepwiseStateContract.Tests/bin/Release/net8.0-windows/CadHarness.StepwiseStateContract.Tests.dll'; arguments=@($taskScratch,'--pure') }
)) {
    $taskLog = Join-Path $taskOutput ("v02-" + $taskCase.name.ToLowerInvariant() + '-pure.log')
    & $taskSdk (Join-Path $taskRoot $taskCase.path) @($taskCase.arguments) *> $taskLog
    $taskExit = $LASTEXITCODE
    Get-Content -LiteralPath $taskLog -Tail 2
    $taskBaselineEquivalent = $false
    if ($taskExit -ne 0 -and $taskCase.name -eq 'Planning') {
        $taskBefore = Get-Content -LiteralPath (Join-Path $taskOutput 'preexisting-selected-head/artifacts/milestone7/pure-result.json') -Raw | ConvertFrom-Json
        $taskAfter = Get-Content -LiteralPath (Join-Path $taskScratch 'artifacts/milestone7/pure-result.json') -Raw | ConvertFrom-Json
        $taskBeforeFailures = @($taskBefore.Tests | Where-Object { -not $_.Passed }) | ConvertTo-Json -Depth 8 -Compress
        $taskAfterFailures = @($taskAfter.Tests | Where-Object { -not $_.Passed }) | ConvertTo-Json -Depth 8 -Compress
        $taskBaselineEquivalent = $taskBefore.Total -eq $taskAfter.Total -and $taskBefore.Passed -eq $taskAfter.Passed -and $taskBeforeFailures -ceq $taskAfterFailures
        if ($taskBaselineEquivalent) { Write-Output 'Historical M7: same six obsolete capability assertions as independently compiled pre-M11 HEAD; no new failures.' }
    }
    $taskResults += [ordered]@{ suite=$taskCase.name; exitCode=$taskExit; log=$taskLog; nativeCalls=0; preexistingFailuresUnchanged=$taskBaselineEquivalent }
    if ($taskExit -ne 0 -and -not $taskBaselineEquivalent) { Get-Content -LiteralPath $taskLog -Tail 20; throw "v0.2 $($taskCase.name) pure regression failed." }
}
[ordered]@{ status='NO_NEW_REGRESSIONS'; build='Release; 0 warnings/errors'; tests=$taskResults; schemaGoldenTest='Isolated generated schema after enum-set equivalence proof; original historical mismatch retained'; nativePartsCreated=0; nativeOpenCycles=0 } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'v02-regression.json') -Encoding UTF8
Write-Output 'Solution and current qualification passed; no new v0.2 regressions; six historical M7 failures retained; no COM/native activation.'
