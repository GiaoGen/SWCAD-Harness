param([string]$InteropDir = 'D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
Push-Location $taskRoot
try {
    & $taskDotnet run --project tests/CadHarness.Playground.Tests -c Release "-p:SolidWorksInteropDir=$InteropDir"
    if ($LASTEXITCODE -ne 0) { throw 'Playground pure/mock tests failed.' }
} finally { Pop-Location }
