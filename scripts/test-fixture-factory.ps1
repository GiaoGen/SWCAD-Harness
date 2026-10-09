[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$env:SOLIDWORKS_INTEROP_DIR='D:\Solidworks Crops\SOLIDWORKS\api\redist'
foreach($name in 'Builder','Tests'){
    & $dotnet build (Join-Path $root "tools/CadFixtureFactory.$name/CadFixtureFactory.$name.csproj") -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
    if($LASTEXITCODE -ne 0){throw "Factory $name compilation failed."}
}
& $dotnet (Join-Path $root 'tools/CadFixtureFactory.Tests/bin/Release/net8.0-windows/CadFixtureFactory.Tests.dll') $root
if($LASTEXITCODE -ne 0){throw 'Factory pure tests failed.'}
