using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record SaidaAutomaticaProdutoAcabadoInput(
    Guid VarianteId, Guid ResponsavelId, int Quantidade, string? Motivo, string ChaveIdempotencia);

public sealed record SaidaAutomaticaProdutoAcabadoResult(
    OperacaoProdutoAcabadoStatus Status,
    IReadOnlyList<MovimentoProdutoAcabado> Movimentos,
    bool Repetida = false,
    string? Erro = null);

public interface IRegistrarSaidaAutomaticaProdutoAcabado
{
    Task<SaidaAutomaticaProdutoAcabadoResult> RegistrarSaidaAutomaticaAsync(
        SaidaAutomaticaProdutoAcabadoInput input, CancellationToken cancellationToken = default);
}
