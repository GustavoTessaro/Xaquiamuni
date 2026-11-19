namespace Xaquiamuni.Aplicacao.Estoque;

public enum OperacaoEstoqueInsumoStatus
{
    Sucesso,
    SemDiferenca,
    OperacaoInvalida,
    InsumoNaoEncontrado,
    LoteNaoEncontrado,
    LoteInelegivel,
    SaldoInsuficiente,
    MovimentoNaoEncontrado,
    MovimentoJaEstornado,
    ConflitoIdempotencia,
    ConflitoConcorrencia,
    MovimentoVinculadoCompra
}
