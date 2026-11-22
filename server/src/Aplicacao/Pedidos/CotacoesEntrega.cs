using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnderecoCotacaoEntrega(
    string? Cep, string? Uf, string? Cidade, string? Bairro, string? Logradouro, string? Numero, string? Complemento);

public sealed record OfertaEntregaResponse(
    string Id, string Modalidade, DateTimeOffset ValidaAteUtc,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long FreteCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TotalCentavos,
    DadosEntregaPedido DadosEntrega);

public sealed record CotacaoEntregaResult(
    IReadOnlyList<OfertaEntregaResponse> Ofertas, IReadOnlyList<PrecoAtualPedido> PrecosAtuais,
    string? MotivoEntregaPropriaIndisponivel = null);

public interface ICotarEntrega
{
    Task<CotacaoEntregaResult> CotarAsync(Guid clienteId, IReadOnlyList<ItemCompraInput> itens,
        Guid? enderecoCadastradoId, EnderecoCotacaoEntrega? endereco, bool usarEnderecoPadrao,
        CancellationToken cancellationToken);
}
