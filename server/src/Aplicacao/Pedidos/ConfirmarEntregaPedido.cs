using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum ConfirmarEntregaPedidoStatus
{
    Entregue,
    Repetida,
    DadosInvalidos,
    AdministradorNaoAutorizado,
    PedidoNaoEncontrado,
    EstadoIncompativel,
    ReservasInconsistentes,
    LoteInelegivel,
    SaldoInsuficiente,
    ConflitoConcorrencia,
}

public sealed record EntregaPedidoConfirmada(
    Guid PedidoId, Guid ResponsavelId, DateTimeOffset EntregueEmUtc, IReadOnlyList<Guid> MovimentoIds);

public sealed record ConfirmarEntregaPedidoResult(
    ConfirmarEntregaPedidoStatus Status, EntregaPedidoConfirmada? Entrega = null);

public interface IConfirmarEntregaPedidoRepository
{
    Task<ConfirmarEntregaPedidoResult> ConfirmarEntregaAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken);
}

public sealed class ConfirmarEntregaPedido(IConfirmarEntregaPedidoRepository repository)
{
    public Task<ConfirmarEntregaPedidoResult> ExecuteAsync(
        Guid pedidoId, Guid administradorId, bool entregaPresencialRealizada, CancellationToken cancellationToken = default) =>
        pedidoId == Guid.Empty || administradorId == Guid.Empty || !entregaPresencialRealizada
            ? Task.FromResult(new ConfirmarEntregaPedidoResult(ConfirmarEntregaPedidoStatus.DadosInvalidos))
            : repository.ConfirmarEntregaAsync(pedidoId, administradorId, cancellationToken);

    public static bool ReservasCorrespondem(
        Pedido pedido, IReadOnlyList<ReservaProdutoAcabado> reservas) =>
        pedido.PagamentoConfirmadoEmUtc is not null
        && IntegridadeReservasPedido.Correspondem(pedido, reservas, pedido.Situacao == SituacaoPedido.Entregue
            ? EstadoReservaProdutoAcabado.Consumida
            : EstadoReservaProdutoAcabado.Confirmada);

    public static bool MovimentosCorrespondem(
        Pedido pedido, IReadOnlyList<ReservaProdutoAcabado> reservas, IReadOnlyList<MovimentoProdutoAcabado> movimentos)
    {
        if (pedido.EncerradoEmUtc is not { } instante || pedido.EncerradoPorId is not { } responsavel
            || movimentos.Count != reservas.Count)
        {
            return false;
        }

        var porReserva = reservas.ToDictionary(reserva => reserva.Id);
        return movimentos.All(movimento => movimento.ReservaProdutoAcabadoId is { } id
            && porReserva.Remove(id, out var reserva)
            && reserva.EncerradaEmUtc == instante
            && movimento.RegistradoEmUtc == instante && movimento.ResponsavelId == responsavel
            && movimento.ProdutoVarianteId == reserva.ProdutoVarianteId && movimento.LoteId == reserva.LoteId
            && movimento.Quantidade == reserva.Quantidade && movimento.Tipo == TipoMovimentoProdutoAcabado.SaidaManual
            && movimento.CondicaoOrigem == CondicaoEstoqueProdutoAcabado.Vendavel
            && movimento.SaldoAnterior.QuantidadeVendavel - movimento.SaldoPosterior.QuantidadeVendavel == reserva.Quantidade);
    }
}
