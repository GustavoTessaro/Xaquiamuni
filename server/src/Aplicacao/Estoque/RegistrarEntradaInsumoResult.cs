using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record RegistrarEntradaInsumoResult(
    RegistrarEntradaInsumoStatus Status,
    MovimentoInsumo? Movimento = null,
    string? Erro = null,
    bool Repetida = false)
{
    public bool Sucesso => Status == RegistrarEntradaInsumoStatus.Sucesso;

    public static RegistrarEntradaInsumoResult Success(
        MovimentoInsumo movimento,
        bool repetida = false) =>
        new(RegistrarEntradaInsumoStatus.Sucesso, movimento, null, repetida);

    public static RegistrarEntradaInsumoResult Failure(
        RegistrarEntradaInsumoStatus status,
        string erro) =>
        new(status, null, erro);
}
