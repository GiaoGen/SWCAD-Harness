[CmdletBinding()]
param([string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InteropDir)) {
    $taskClsid = (Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID').GetValue('')
    $taskServer = (Get-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CLSID\$taskClsid\LocalServer32").GetValue('').Trim('"')
    $InteropDir = Join-Path (Split-Path -Parent $taskServer) 'api/redist'
}
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Bootstrap -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.Bootstrap.Tests/bin/Release/net8.0/CadHarness.Bootstrap.Tests.dll') $InteropDir
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap pure runner failed.' }
