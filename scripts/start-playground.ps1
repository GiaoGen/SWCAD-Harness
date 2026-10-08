param(
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR,
    [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE,
    [ValidateRange(1024,65535)][int]$Port = 5186
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
if (-not $InteropDir) { $InteropDir = 'D:\Solidworks Crops\SOLIDWORKS\api\redist' }
$env:SOLIDWORKS_INTEROP_DIR = $InteropDir
$env:CAD_HARNESS_PART_TEMPLATE = $PartTemplate
$env:CAD_HARNESS_PLAYGROUND_PORT = "$Port"
Push-Location $taskRoot
try { & $taskDotnet run --project src/CadHarness.Playground -c Release; if ($LASTEXITCODE -ne 0) { throw 'Playground host failed.' } }
finally { Pop-Location }
