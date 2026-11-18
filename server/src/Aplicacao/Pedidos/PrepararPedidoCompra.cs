using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum EtapaPreparacaoPedido
{
    IniciarPreparacao,
    LiberarParaRetirada,
}

public enum PrepararPedidoStatus
{
    Sucesso,
    Repetida,
    DadosInvalidos,
    AdministradorNaoAutorizado,
    PedidoNaoEncontrado,
    EstadoIncompativel,
    ReservasInconsistentes,
    ConflitoConcorrencia,
}

public interface IPrepararPedidoCompraRepository
{
    Task<PrepararPedidoStatus> AvancarAsync(
        Guid pedidoId, Guid administradorId, EtapaPreparacaoPedido etapa, CancellationToken cancellationToken);
}

public sealed class PrepararPedidoCompra(IPrepararPedidoCompraRepository repository)
{
    public Task<PrepararPedidoStatus> IniciarPreparacaoAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken = default) =>
        ExecutarAsync(pedidoId, administradorId, EtapaPreparacaoPedido.IniciarPreparacao, cancellationToken);

    public Task<PrepararPedidoStatus> LiberarParaRetiradaAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken = default) =>
        ExecutarAsync(pedidoId, administradorId, EtapaPreparacaoPedido.LiberarParaRetirada, cancellationToken);

    private Task<PrepararPedidoStatus> ExecutarAsync(
        Guid pedidoId, Guid administradorId, EtapaPreparacaoPedido etapa, CancellationToken cancellationToken) =>
        pedidoId == Guid.Empty || administradorId == Guid.Empty
            ? Task.FromResult(PrepararPedidoStatus.DadosInvalidos)
            : repository.AvancarAsync(pedidoId, administradorId, etapa, cancellationToken);

    // Chamado sob os locks do pedido e das variantes, com o mesmo UTC usado na auditoria.
    public static PrepararPedidoStatus Aplicar(
        Pedido pedido, IReadOnlyList<ReservaProdutoAcabado> reservas,
        Guid administradorId, DateTimeOffset agoraUtc, EtapaPreparacaoPedido etapa)
    {
        var (origem, destino) = etapa switch
        {
            EtapaPreparacaoPedido.IniciarPreparacao => (SituacaoPedido.PagamentoConfirmado, SituacaoPedido.EmPreparacao),
            EtapaPreparacaoPedido.LiberarParaRetirada => (SituacaoPedido.EmPreparacao, SituacaoPedido.ProntoParaRetirada),
            _ => throw new ArgumentOutOfRangeException(nameof(etapa)),
        };

        if (pedido.Situacao != origem && pedido.Situacao != destino)
        {
            return PrepararPedidoStatus.EstadoIncompativel;
        }

        if (pedido.PagamentoConfirmadoEmUtc is null
            || !IntegridadeReservasPedido.Correspondem(pedido, reservas, EstadoReservaProdutoAcabado.Confirmada))
        {
            return PrepararPedidoStatus.ReservasInconsistentes;
        }

        if (pedido.Situacao == destino)
        {
            return PrepararPedidoStatus.Repetida;
        }

        if (etapa == EtapaPreparacaoPedido.IniciarPreparacao)
        {
            pedido.IniciarPreparacao(administradorId, agoraUtc);
        }
        else
        {
            pedido.AutorizarRetirada(administradorId, agoraUtc);
        }

        return PrepararPedidoStatus.Sucesso;
    }
}
