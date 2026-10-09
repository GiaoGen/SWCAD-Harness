[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$env:SOLIDWORKS_INTEROP_DIR='D:\Solidworks Crops\SOLIDWORKS\api\redist'
& $dotnet build (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/CadHarness.ExternalEditing.Tests.csproj') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'M14 build failed.'}
& $dotnet (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/bin/Release/net8.0-windows/CadHarness.ExternalEditing.Tests.dll') $root --pure
if($LASTEXITCODE -ne 0){throw 'M14 pure tests failed.'}
