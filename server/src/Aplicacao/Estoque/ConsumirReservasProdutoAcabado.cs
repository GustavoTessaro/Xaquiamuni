using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public enum ConsumirReservasStatus
{
    Sucesso,
    Repetida,
    SolicitacaoInvalida,
    ReservasNaoEncontradas,
    EstadoIncompativel,
    LoteInelegivel,
    SaldoInsuficiente,
    ConflitoConcorrencia
}

public sealed record ConsumirReservasResult(
    ConsumirReservasStatus Status,
    IReadOnlyList<MovimentoProdutoAcabado> Movimentos);

public interface IConsumirReservasProdutoAcabado
{
    // O futuro chamador deve verificar autorização e que o pedido está apto para retirada.
    Task<ConsumirReservasResult> ConsumirAsync(
        Guid pedidoId, Guid responsavelId, CancellationToken cancellationToken = default);
}
