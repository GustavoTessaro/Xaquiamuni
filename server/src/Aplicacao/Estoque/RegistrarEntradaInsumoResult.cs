using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record RegistrarEntradaInsumoResult(
    RegistrarEntradaInsumoStatus Status,
    MovimentoInsumo? Movimento = null,
    string? Erro = null)
{
    public bool Sucesso => Status == RegistrarEntradaInsumoStatus.Sucesso;

    public static RegistrarEntradaInsumoResult Success(MovimentoInsumo movimento) =>
        new(RegistrarEntradaInsumoStatus.Sucesso, movimento);

    public static RegistrarEntradaInsumoResult Failure(
        RegistrarEntradaInsumoStatus status,
        string erro) =>
        new(status, null, erro);
}
