[CmdletBinding()]
param([ValidateSet('Pure','Prepare','Native','Cold')][string]$Mode='Pure',
      [string]$RunId='acceptance_v1',
      [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root "artifacts/milestone18/frozen-bin-$RunId"
$runner=Join-Path $bin 'CadHarness.QueryEditing.Tests.dll'
if($Mode -in @('Pure','Prepare')) {
    if(Test-Path -LiteralPath $bin){throw 'Do not overwrite a frozen binary namespace'}
    & $sdk build (Join-Path $root 'tests/CadHarness.QueryEditing.Tests/CadHarness.QueryEditing.Tests.csproj') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false "-p:SolidWorksInteropDir=$InteropDir" -o $bin
    if($LASTEXITCODE -ne 0){throw 'M18 build failed'}
    & $sdk $runner $root --pure ('pure_'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))
    if($LASTEXITCODE -ne 0){throw 'M18 pure verification failed'}
    if($Mode -eq 'Prepare'){
        & $sdk $runner $root --prepare $RunId
        if($LASTEXITCODE -ne 0){throw 'M18 freeze failed'}
    }
    return
}
if(-not (Test-Path -LiteralPath $runner)){throw 'Prepare and freeze first; native execution never rebuilds'}
$phase=if($Mode -eq 'Cold'){'--cold'}else{'--native'}
& $sdk $runner $root $phase $RunId
if($LASTEXITCODE -ne 0){throw 'M18 failed; preserve evidence and stop, no blind rerun'}
