[CmdletBinding()]
param(
    [ValidateSet('Pure','Native')][string]$Mode = 'Pure',
    [string]$RunId,
    [string]$OwnedBlankPart,
    [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE,
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk = Initialize-CadHarnessSdk -Root $root
if ([string]::IsNullOrWhiteSpace($InteropDir)) {
    $clsid = (Get-Item 'Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID').GetValue('')
    $server = (Get-Item "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid\LocalServer32").GetValue('').Trim('"')
    $InteropDir = Join-Path (Split-Path -Parent $server) 'api/redist'
}
$bin = Join-Path $root 'artifacts/milestone15/bin'
& $sdk build (Join-Path $root 'tests/CadHarness.PlanarSketch.Tests/CadHarness.PlanarSketch.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M15 build failed.' }
$runner = Join-Path $bin 'CadHarness.PlanarSketch.Tests.dll'
if ($Mode -eq 'Pure') { & $sdk $runner $root --pure }
else {
    if ([string]::IsNullOrWhiteSpace($RunId)) { throw 'Native runs need a unique, never-reused RunId.' }
    if (![string]::IsNullOrWhiteSpace($PartTemplate)) { $env:CAD_HARNESS_PART_TEMPLATE = $PartTemplate }
    if ([string]::IsNullOrWhiteSpace($OwnedBlankPart)) { & $sdk $runner $root --native $RunId }
    else { & $sdk $runner $root --native-copy $RunId $OwnedBlankPart }
}
if ($LASTEXITCODE -ne 0) { throw "M15 $Mode failed; preserve its evidence and diagnose before another native slot." }
