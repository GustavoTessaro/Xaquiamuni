using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record OperacaoEstoqueInsumoResult(
    OperacaoEstoqueInsumoStatus Status,
    MovimentoInsumo? Movimento = null,
    string? Erro = null)
{
    public bool Sucesso => Status is
        OperacaoEstoqueInsumoStatus.Sucesso or OperacaoEstoqueInsumoStatus.SemDiferenca;

    public static OperacaoEstoqueInsumoResult Success(MovimentoInsumo movimento) =>
        new(OperacaoEstoqueInsumoStatus.Sucesso, movimento);

    public static OperacaoEstoqueInsumoResult NoDifference() =>
        new(OperacaoEstoqueInsumoStatus.SemDiferenca);

    public static OperacaoEstoqueInsumoResult Failure(
        OperacaoEstoqueInsumoStatus status,
        string erro) =>
        new(status, null, erro);
}
