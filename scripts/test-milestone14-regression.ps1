[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$env:SOLIDWORKS_INTEROP_DIR='D:\Solidworks Crops\SOLIDWORKS\api\redist'
& $dotnet build (Join-Path $root 'CadHarness.sln') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'M14 solution build failed.'}
$output=Join-Path $root ('artifacts/milestone14/regression/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Path $output | Out-Null
foreach($folder in @('schemas','docs','src')){Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $output -Recurse}
New-Item -ItemType Directory -Path (Join-Path $output 'tests') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $output 'artifacts') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'artifacts/milestone9g') -Destination (Join-Path $output 'artifacts') -Recurse
New-Item -ItemType Directory -Path (Join-Path $output 'artifacts/milestone10b') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'artifacts/milestone10b/call-01-request.json') -Destination (Join-Path $output 'artifacts/milestone10b')
$runs=@()
foreach($name in @('CadHarness.V03Contracts.Tests','CadHarness.ManagedRecovery.Tests','CadHarness.ExternalObservation.Tests','CadHarness.State.Tests','CadHarness.Transactions.Tests','CadHarness.ConstructionTransactions.Tests')){
    $fixture=Join-Path $root "tests/$name/Fixtures"
    if(Test-Path -LiteralPath $fixture){New-Item -ItemType Directory -Path (Join-Path $output "tests/$name") -Force|Out-Null;Copy-Item -LiteralPath $fixture -Destination (Join-Path $output "tests/$name") -Recurse}
    $framework=if($name -eq 'CadHarness.V03Contracts.Tests'){'net8.0'}else{'net8.0-windows'}
    & $dotnet (Join-Path $root "tests/$name/bin/Release/$framework/$name.dll") $output --pure 2>&1|Tee-Object -FilePath (Join-Path $output "$name.log")
    $code=$LASTEXITCODE
    $runs+=[ordered]@{suite=$name;exitCode=$code}
    if($code -ne 0){$runs|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'failed-result.json');throw "M14 regression failed: $name"}
}
$runs|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding UTF8
Write-Output "M14 isolated regressions: $output"
