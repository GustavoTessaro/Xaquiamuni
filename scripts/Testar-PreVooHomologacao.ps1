param(
    [Parameter(Mandatory)][string]$ServidorSqlEsperado,
    [Parameter(Mandatory)][string]$BancoSqlEsperado
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$ferramenta = Join-Path $raiz 'server/tools/Xaquiamuni.PreVoo/bin/Release/net10.0/Xaquiamuni.PreVoo.dll'
if (-not (Test-Path -LiteralPath $ferramenta -PathType Leaf)) {
    Write-Output '[BLOQUEIO] Ferramenta — Ação: compile server/Xaquiamuni.slnx em Release antes de executar o pré-voo.'
    exit 2
}
dotnet $ferramenta $ServidorSqlEsperado $BancoSqlEsperado (Join-Path $raiz 'server/src/Api')
exit $LASTEXITCODE
