using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ResumoPedidoAdmin(
    Guid Id, SituacaoPedido Situacao, DateTimeOffset CriadoEmUtc, DateTimeOffset PrazoPagamentoEmUtc,
    long TotalCentavos, string NomeCliente);

public sealed record DetalhePedidoAdmin(Pedido Pedido, string NomeCliente, string? EmailCliente);

public sealed record PaginaPedidosAdmin(IReadOnlyList<ResumoPedidoAdmin> Itens, int TotalItens);

public interface IConsultarPedidosAdmin
{
    Task<PaginaPedidosAdmin> ListarAsync(
        SituacaoPedido? situacao, int deslocamento, int limite, CancellationToken cancellationToken);

    Task<DetalhePedidoAdmin?> BuscarAsync(Guid pedidoId, CancellationToken cancellationToken);
}
