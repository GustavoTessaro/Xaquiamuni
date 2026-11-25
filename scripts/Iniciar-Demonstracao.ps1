$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$analyticsAnterior = $env:NG_CLI_ANALYTICS
$sessao = [Guid]::NewGuid().ToString('N')
$inicio = [DateTimeOffset]::Now.ToString('o')
$artefatos = $null
$preservarArtefatos = $false
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $branch = git branch --show-current
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not $branch) { $branch = 'HEAD destacado' }
    $head = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $estado = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $alteracoes = if ($estado.Count) { 'sim' } else { 'nao' }
    $raizTemp = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd('\', '/')
    $alvo = Join-Path $raizTemp "xaquiamuni-demo-build-$sessao"
    New-Item -ItemType Directory -Path $alvo -ErrorAction Stop | Out-Null
    $artefatos = $alvo
    $env:NG_CLI_ANALYTICS = 'false'
    $angular = Join-Path $artefatos 'angular'
    Write-Host "Compilando demonstracao $sessao..."
    npm.cmd --prefix client run build -- --configuration production --progress=false --output-path $angular
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $browser = Join-Path $angular 'browser'
    if (-not (Test-Path -LiteralPath (Join-Path $browser 'index.html') -PathType Leaf)) {
        throw 'Build Angular incompleto.'
    }
    $backend = Join-Path $artefatos 'dotnet'
    dotnet build server/tools/Xaquiamuni.Demonstracao -c Release --tl:off --artifacts-path $backend --verbosity quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $executavel = Join-Path $backend 'bin/Xaquiamuni.Demonstracao/release/Xaquiamuni.Demonstracao.dll'
    if (-not (Test-Path -LiteralPath $executavel -PathType Leaf)) { throw 'Build .NET incompleto.' }
    $headAtual = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $headAtual -ne $head) { throw 'HEAD mudou durante a compilacao.' }
    dotnet $executavel $browser $sessao $inicio $branch $head $alteracoes
    $resultado = $LASTEXITCODE
    $preservarArtefatos = $resultado -eq 2
    exit $resultado
}
finally {
    $env:NG_CLI_ANALYTICS = $analyticsAnterior
    Pop-Location
    if ($artefatos -and -not $preservarArtefatos) {
        $resolvido = (Resolve-Path -LiteralPath $artefatos).Path
        if ($resolvido -ne $artefatos -or (Split-Path -Parent $resolvido) -ne $raizTemp -or
            (Split-Path -Leaf $resolvido) -ne "xaquiamuni-demo-build-$sessao") {
            throw 'Diretorio de compilacao inesperado. Limpeza recusada.'
        }
        Remove-Item -LiteralPath $resolvido -Recurse -Force
    }
}
