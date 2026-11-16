using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record OperacaoEstoqueInsumoResult(
    OperacaoEstoqueInsumoStatus Status,
    MovimentoInsumo? Movimento = null,
    string? Erro = null,
    bool Repetida = false)
{
    public bool Sucesso => Status is
        OperacaoEstoqueInsumoStatus.Sucesso or OperacaoEstoqueInsumoStatus.SemDiferenca;

    public static OperacaoEstoqueInsumoResult Success(
        MovimentoInsumo movimento,
        bool repetida = false) =>
        new(OperacaoEstoqueInsumoStatus.Sucesso, movimento, null, repetida);

    public static OperacaoEstoqueInsumoResult NoDifference(bool repetida = false) =>
        new(OperacaoEstoqueInsumoStatus.SemDiferenca, null, null, repetida);

    public static OperacaoEstoqueInsumoResult Failure(
        OperacaoEstoqueInsumoStatus status,
        string erro) =>
        new(status, null, erro);
}
