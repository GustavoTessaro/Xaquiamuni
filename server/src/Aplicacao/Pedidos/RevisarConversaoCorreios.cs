using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ItemRevisaoConversaoCorreios(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade,
    string NomeProduto, string NomeVariante, bool Disponivel, PrecoAtualPedido? Preco);

public sealed record RevisaoConversaoCorreios(DadosPropostaPostal Proposta, EnderecoEntregaPedido Endereco,
    IReadOnlyList<ItemRevisaoConversaoCorreios> Itens,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long FreteCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? SubtotalProdutosCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? TotalCentavos,
    string? ConversaoCorreiosId);

public interface IRevisarConversaoCorreios
{
    Task<CriarPedidoCompraResult> RevisarAsync(Guid clienteId, Guid cotacaoId, CancellationToken ct);
}
