$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$analyticsAnterior = $env:NG_CLI_ANALYTICS
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $env:NG_CLI_ANALYTICS = 'false'
    npm.cmd --prefix client run build -- --configuration production
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet run --project server/tools/Xaquiamuni.Demonstracao -c Release --tl:off
    exit $LASTEXITCODE
}
finally {
    $env:NG_CLI_ANALYTICS = $analyticsAnterior
    Pop-Location
}
