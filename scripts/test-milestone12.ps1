[CmdletBinding()]
param([string]$SolidWorksInteropDir = 'D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
$env:SOLIDWORKS_INTEROP_DIR = $SolidWorksInteropDir
& $taskDotnet build (Join-Path $taskRoot 'CadHarness.sln') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'M12 solution build failed.' }
& $taskDotnet build (Join-Path $taskRoot 'tests/CadHarness.StepwiseStateContract.Tests/CadHarness.StepwiseStateContract.Tests.csproj') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Current qualification build failed.' }
& $taskDotnet (Join-Path $taskRoot 'tests/CadHarness.ManagedRecovery.Tests/bin/Release/net8.0-windows/CadHarness.ManagedRecovery.Tests.dll') $taskRoot --pure
if ($LASTEXITCODE -ne 0) { throw 'M12 pure durability suite failed.' }
# Historical test programs write under their supplied root. Use an isolated
# fixture copy so their existing evidence can never be overwritten.
$taskRegression = Join-Path $taskRoot ('artifacts/milestone12/regression/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Path $taskRegression | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'schemas') -Destination $taskRegression -Recurse
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $taskRegression -Recurse
Copy-Item -LiteralPath (Join-Path $taskRoot 'src') -Destination $taskRegression -Recurse
New-Item -ItemType Directory -Path (Join-Path $taskRegression 'tests') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $taskRegression 'artifacts') | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'artifacts/milestone9g') -Destination (Join-Path $taskRegression 'artifacts') -Recurse
New-Item -ItemType Directory -Path (Join-Path $taskRegression 'artifacts/milestone10b') | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'artifacts/milestone10b/call-01-request.json') -Destination (Join-Path $taskRegression 'artifacts/milestone10b')
foreach ($taskName in @('CadHarness.V03Contracts.Tests','CadHarness.State.Tests','CadHarness.Transactions.Tests','CadHarness.ConstructionTransactions.Tests','CadHarness.StepwiseStateContract.Tests')) {
    $taskSource = Join-Path $taskRoot ('tests/' + $taskName)
    $taskFixture = Join-Path $taskSource 'Fixtures'
    if (Test-Path -LiteralPath $taskFixture) {
        New-Item -ItemType Directory -Path (Join-Path $taskRegression ('tests/' + $taskName)) -Force | Out-Null
        Copy-Item -LiteralPath $taskFixture -Destination (Join-Path $taskRegression ('tests/' + $taskName)) -Recurse
    }
    $taskFramework = if ($taskName -eq 'CadHarness.V03Contracts.Tests') { 'net8.0' } else { 'net8.0-windows' }
    $taskDll = Join-Path $taskSource ('bin/Release/' + $taskFramework + '/' + $taskName + '.dll')
    & $taskDotnet $taskDll $taskRegression --pure
    if ($LASTEXITCODE -ne 0) { throw "Regression suite failed: $taskName; evidence: $taskRegression" }
}
Write-Output "M12 pure and isolated regression evidence: $taskRegression; no native calls."
