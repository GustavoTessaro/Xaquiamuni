using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ResumoPedidoAdmin(
    Guid Id, SituacaoPedido Situacao, DateTimeOffset CriadoEmUtc, DateTimeOffset PrazoPagamentoEmUtc,
    long TotalCentavos, string NomeCliente);

public sealed record DetalhePedidoAdmin(Pedido Pedido, string NomeCliente, string? EmailCliente);

public sealed record PaginaPedidosAdmin(IReadOnlyList<ResumoPedidoAdmin> Itens, int TotalItens);

public sealed record CorrecaoRastreamentoAdmin(string? CodigoAnterior, string? CodigoNovo,
    DateTimeOffset RealizadaEmUtc, Guid AdministradorId);

public interface IConsultarPedidosAdmin
{
    Task<PaginaPedidosAdmin> ListarAsync(
        SituacaoPedido? situacao, int deslocamento, int limite, CancellationToken cancellationToken);

    Task<DetalhePedidoAdmin?> BuscarAsync(Guid pedidoId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CorrecaoRastreamentoAdmin>?> ConsultarHistoricoRastreamentoAsync(
        Guid pedidoId, CancellationToken cancellationToken);
}
