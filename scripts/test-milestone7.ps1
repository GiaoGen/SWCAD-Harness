[CmdletBinding()]
param([string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone7 -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.Planning.Tests/bin/Release/net8.0-windows/CadHarness.Planning.Tests.dll'
& $taskSdk $taskAssembly $taskRoot
if ($LASTEXITCODE -ne 0) { throw "Milestone 7 runner failed with exit code $LASTEXITCODE." }
