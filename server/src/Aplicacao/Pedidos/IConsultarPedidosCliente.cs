using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ResumoPedidoCliente(
    Guid Id, SituacaoPedido Situacao, DateTimeOffset CriadoEmUtc,
    DateTimeOffset PrazoPagamentoEmUtc, long TotalCentavos);

public sealed record PaginaPedidosCliente(IReadOnlyList<ResumoPedidoCliente> Itens, int TotalItens);

public interface IConsultarPedidosCliente
{
    Task<PaginaPedidosCliente> ListarAsync(Guid clienteId, int deslocamento, int limite, CancellationToken cancellationToken);
    Task<Pedido?> BuscarAsync(Guid clienteId, Guid pedidoId, CancellationToken cancellationToken);
}
