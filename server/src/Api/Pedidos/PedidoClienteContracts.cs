using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Pedidos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CriarPedidoClienteRequest(ItemPedidoClienteRequest?[]? Itens);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemPedidoClienteRequest(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade,
    string? PrecoAceitoCentavos = null);

public sealed record PedidoClienteResumoResponse(
    Guid Id, string Situacao, DateTimeOffset CriadoEmUtc, DateTimeOffset PrazoPagamentoEmUtc,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TotalCentavos);

public sealed record PedidoClientePaginaResponse(
    IReadOnlyList<PedidoClienteResumoResponse> Itens, int Pagina, int ItensPorPagina, int TotalItens, int TotalPaginas);

public sealed record PedidoClienteResponse(
    Guid Id, string Situacao, string ModalidadeEntrega,
    DateTimeOffset CriadoEmUtc, DateTimeOffset PrazoPagamentoEmUtc,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TotalCentavos,
    IReadOnlyList<ItemPedidoClienteResponse> Itens)
{
    internal static PedidoClienteResponse From(Pedido pedido) => new(
        pedido.Id, pedido.Situacao.ToString(), pedido.ModalidadeEntrega.ToString(),
        pedido.CriadoEmUtc, pedido.PrazoPagamentoEmUtc, pedido.TotalCentavos,
        pedido.Itens.OrderBy(item => item.ProdutoVarianteId)
            .Select(item => new ItemPedidoClienteResponse(item.ProdutoId, item.ProdutoVarianteId,
                item.NomeProduto, item.NomeVariante, item.Quantidade,
                item.PrecoUnitarioCentavos, item.SubtotalCentavos, item.PrecoRegularCentavos, item.PromocaoId)).ToArray());
}

public sealed record ItemPedidoClienteResponse(
    Guid ProdutoId, Guid ProdutoVarianteId, string NomeProduto, string NomeVariante, int Quantidade,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long PrecoUnitarioCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long SubtotalCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? PrecoRegularCentavos = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PromocaoId = null);
