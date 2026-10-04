function Initialize-CadHarnessSdk {
    param([Parameter(Mandatory)][string]$Root)
    $taskExe = Join-Path $Root 'artifacts/toolchain/dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $taskExe)) { $taskExe = (Get-Command dotnet -ErrorAction Stop).Source }
    $taskRequired = (Get-Content -LiteralPath (Join-Path $Root 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $taskAvailable = @(& $taskExe --list-sdks)
    if (-not ($taskAvailable -match "^$([regex]::Escape($taskRequired)) ")) {
        throw "Pinned SDK $taskRequired is missing. Run scripts/setup-dotnet.ps1; no automatic system installation is performed."
    }
    $env:DOTNET_CLI_HOME = Join-Path $Root 'artifacts/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $Root 'artifacts/nuget-packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    return $taskExe
}
