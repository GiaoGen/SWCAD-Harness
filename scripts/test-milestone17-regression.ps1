[CmdletBinding()]
param([string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$output=Join-Path $root ('artifacts/milestone17/regression/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
$bin=Join-Path $output 'bin'
New-Item -ItemType Directory -Path $output | Out-Null
& $sdk build (Join-Path $root 'CadHarness.sln') -c Release -m:1 --disable-build-servers '-p:NuGetAudit=false' "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'M17 solution build failed.' }
foreach ($folder in @('schemas','docs')) { Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $output -Recurse }
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }) {
    $target=Join-Path $output ([IO.Path]::GetRelativePath($root,$source.FullName))
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source.FullName -Destination $target
}
$suites=@('CadHarness.Ir.Tests','CadHarness.Planning.Tests','CadHarness.V03Contracts.Tests','CadHarness.ManagedRecovery.Tests',
    'CadHarness.ExternalObservation.Tests','CadHarness.ExternalEditing.Tests','CadHarness.State.Tests','CadHarness.Transactions.Tests',
    'CadHarness.ConstructionTransactions.Tests','CadHarness.PlanarSketch.Tests','CadHarness.FeatureConstruction.Tests','CadHarness.ConstructionPlanning.Tests')
$runs=@()
foreach ($name in $suites) {
    $fixtures=Join-Path $root "tests/$name/Fixtures"
    if (Test-Path -LiteralPath $fixtures) {
        New-Item -ItemType Directory -Path (Join-Path $output "tests/$name") -Force | Out-Null
        Copy-Item -LiteralPath $fixtures -Destination (Join-Path $output "tests/$name") -Recurse
    }
    [string[]]$arguments=if ($name -in @('CadHarness.Ir.Tests','CadHarness.Planning.Tests','CadHarness.V03Contracts.Tests')) { @($output) } else { @($output,'--pure') }
    & $sdk (Join-Path $bin "$name.dll") @arguments 2>&1 | Tee-Object -FilePath (Join-Path $output "$name.log")
    $code=$LASTEXITCODE
    $runs+=[ordered]@{suite=$name;exitCode=$code}
    $runs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'suite-progress.json') -Encoding UTF8
    if ($code -ne 0) { throw "M17 isolated regression failed: $name; preserve $output" }
}
$runs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'suite-results.json') -Encoding UTF8
Write-Output "M17 isolated regressions passed: $output; no native calls or live provider calls."
