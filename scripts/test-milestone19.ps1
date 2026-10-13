[CmdletBinding()]
param([ValidateSet('Api','All')][string]$Mode='Api',
      [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist',
      [string]$NodeExe='C:\Users\giaog\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe',
      [string]$PlaywrightPackage='C:\Users\giaog\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules\playwright',
      [string]$BrowserExecutable='C:\Program Files\Google\Chrome\Application\chrome.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$output=Join-Path $root ('artifacts/milestone19/regression/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Path $output | Out-Null
$protected=@{}
foreach($milestone in 14..20){
    $p=Join-Path $root "artifacts/milestone$milestone/native-budget.json"
    if(Test-Path -LiteralPath $p){$protected[$p]=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
}
$server=$null
try{
    & $sdk build (Join-Path $root 'tests/CadHarness.Playground.Tests') -c Release "-p:SolidWorksInteropDir=$InteropDir" --no-restore *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'M19 build failed; preserve logs.'}
    $exe=Join-Path $root 'tests/CadHarness.Playground.Tests/bin/Release/net8.0-windows/CadHarness.Playground.Tests.exe'
    & $exe *> (Join-Path $output 'api.log')
    if($LASTEXITCODE -ne 0){throw 'M19 pure/mock API qualification failed; no retry.'}
    if($Mode -eq 'All'){
        if(Get-NetTCPConnection -LocalPort 5188 -State Listen -ErrorAction SilentlyContinue){throw 'Mock UI port occupied; never adopt another server.'}
        $server=Start-Process -FilePath $exe -ArgumentList '--modern-serve' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'mock-server.log') -RedirectStandardError (Join-Path $output 'mock-server-error.log')
        $env:PLAYWRIGHT_PACKAGE=$PlaywrightPackage;$env:BROWSER_EXECUTABLE=$BrowserExecutable
        & $NodeExe (Join-Path $root 'tests/CadHarness.Playground.Tests/browser.cjs') *> (Join-Path $output 'ui.log')
        if($LASTEXITCODE -ne 0){throw 'M19 mock UI qualification failed; preserve evidence and stop.'}
    }
    foreach($entry in $protected.GetEnumerator()){
        if((Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash -ne $entry.Value){throw "Historical native ledger changed: $($entry.Key)"}
    }
    $files=Get-ChildItem (Join-Path $root 'src/CadHarness.Playground'),(Join-Path $root 'tests/CadHarness.Playground.Tests') -File -Recurse |
        Where-Object{$_.FullName -notmatch '\\(bin|obj)\\'} | ForEach-Object{@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}
    @{verdict='PASS';mode=$Mode;nativeParts=0;nativeOpens=0;nativeCloses=0;realProviderCalls=0;protectedLedgers=$protected;files=$files} |
        ConvertTo-Json -Depth 6 | Out-File -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8 -NoClobber
    Write-Output "M19 pure/mock qualification PASS: $output. Native budget 0 Parts / 0 opens."
}finally{
    if($server -and -not $server.HasExited){
        $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($server.Id)"
        if($actual.ExecutablePath -eq $exe -and $actual.CommandLine -like '*--modern-serve*'){Stop-Process -Id $server.Id}
        else{throw 'Mock server identity changed; refuse to stop another process.'}
    }
}
