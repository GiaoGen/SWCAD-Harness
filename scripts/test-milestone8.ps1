[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone8
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.Judging.Tests/bin/Release/net8.0/CadHarness.Judging.Tests.dll'
& $taskSdk $taskAssembly $taskRoot
if ($LASTEXITCODE -ne 0) { throw "Milestone 8 runner failed with exit code $LASTEXITCODE." }
