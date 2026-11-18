using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum ConfirmarPagamentoPedidoStatus
{
    Confirmado,
    Repetido,
    DadosInvalidos,
    AdministradorNaoAutorizado,
    PedidoNaoEncontrado,
    EstadoIncompativel,
    PrazoEncerrado,
    ReservasInconsistentes,
    ConflitoConcorrencia,
}

public interface IConfirmarPagamentoPedidoRepository
{
    Task<ConfirmarPagamentoPedidoStatus> ConfirmarAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken);
}

/// <summary>Registra a decisão administrativa tomada após a conferência bancária humana.</summary>
public sealed class ConfirmarPagamentoPedido(IConfirmarPagamentoPedidoRepository repository)
{
    public Task<ConfirmarPagamentoPedidoStatus> ExecuteAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken = default) =>
        pedidoId == Guid.Empty || administradorId == Guid.Empty
            ? Task.FromResult(ConfirmarPagamentoPedidoStatus.DadosInvalidos)
            : repository.ConfirmarAsync(pedidoId, administradorId, cancellationToken);

    // Chamado sob os locks do pedido e das variantes. A persistência confirma o pedido na mesma transação.
    public static ConfirmarPagamentoPedidoStatus PrepararReservas(
        Pedido pedido, IReadOnlyList<ReservaProdutoAcabado> reservas, DateTimeOffset agoraUtc)
    {
        var repeticao = pedido.Situacao == SituacaoPedido.PagamentoConfirmado;
        if (!repeticao && pedido.Situacao != SituacaoPedido.AguardandoPagamento)
        {
            return ConfirmarPagamentoPedidoStatus.EstadoIncompativel;
        }

        if (!repeticao && agoraUtc >= pedido.PrazoPagamentoEmUtc)
        {
            return ConfirmarPagamentoPedidoStatus.PrazoEncerrado;
        }

        var estadoEsperado = repeticao ? EstadoReservaProdutoAcabado.Confirmada : EstadoReservaProdutoAcabado.Ativa;
        var quantidades = reservas.GroupBy(reserva => reserva.ProdutoVarianteId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(reserva => (long)reserva.Quantidade));
        if (pedido.Itens.Count == 0 || quantidades.Count != pedido.Itens.Count
            || pedido.Itens.Any(item => quantidades.GetValueOrDefault(item.ProdutoVarianteId) != item.Quantidade)
            || reservas.Any(reserva => reserva.PedidoId != pedido.Id || reserva.Estado != estadoEsperado
                || reserva.CriadaEmUtc != pedido.CriadoEmUtc || reserva.ExpiraEmUtc != pedido.PrazoPagamentoEmUtc
                || reserva.ConfirmadaEmUtc != pedido.PagamentoConfirmadoEmUtc))
        {
            return ConfirmarPagamentoPedidoStatus.ReservasInconsistentes;
        }

        if (repeticao)
        {
            return ConfirmarPagamentoPedidoStatus.Repetido;
        }

        return CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Confirmar, agoraUtc)
            == TransicaoReservasStatus.Sucesso
            ? ConfirmarPagamentoPedidoStatus.Confirmado
            : ConfirmarPagamentoPedidoStatus.ReservasInconsistentes;
    }
}
