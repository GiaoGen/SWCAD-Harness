[CmdletBinding()]
param([switch]$WriteSchemas)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskProject = Join-Path $taskRoot 'tests/CadHarness.V03Contracts.Tests/CadHarness.V03Contracts.Tests.csproj'
& $taskSdk build $taskProject --configuration Release --nologo --disable-build-servers '-p:NuGetAudit=false' '-m:1'
if ($LASTEXITCODE -ne 0) { throw 'M11 contract build failed.' }
$taskDll = Join-Path $taskRoot 'tests/CadHarness.V03Contracts.Tests/bin/Release/net8.0/CadHarness.V03Contracts.Tests.dll'
if ($WriteSchemas) {
    & $taskSdk $taskDll $taskRoot '--write-schemas'
    if ($LASTEXITCODE -ne 0) { throw 'M11 schema generation failed.' }
}
& $taskSdk $taskDll $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'M11 pure contract verification failed.' }
Write-Output 'M11 pure verification passed; SOLIDWORKS launches=0; Parts created/opened/closed=0/0/0.'
