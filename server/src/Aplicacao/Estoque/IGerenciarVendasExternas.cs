using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CriarVendaExternaInput(string Canal, string? DescricaoCanal, DateOnly DataVenda, string? Comprador,
    DateOnly? Vencimento, IReadOnlyList<ItemVendaExternaInput>? Itens, ReceberVendaExternaInput? RecebimentoInicial);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemVendaExternaInput(Guid ProdutoId, Guid VarianteId, int Quantidade,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long PrecoUnitarioCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long DescontoCentavos = 0);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceberVendaExternaInput(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ValorCentavos, DateOnly Data, string Meio);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelarVendaExternaInput(string? Motivo);

public sealed record ResumoVendaExterna(Guid Id, Guid Versao, DateOnly DataVenda, string Canal,
    string Situacao, string SituacaoPagamento, string TotalCentavos, string RecebidoCentavos, string SaldoCentavos)
{
    public static ResumoVendaExterna De(VendaExterna v) => new(v.Id, v.Versao, v.DataVenda, v.Canal.ToString(),
        v.Situacao.ToString(), v.SituacaoPagamento, v.TotalCentavos.ToString(System.Globalization.CultureInfo.InvariantCulture),
        v.RecebidoCentavos.ToString(System.Globalization.CultureInfo.InvariantCulture),
        v.SaldoCentavos.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public sealed record DetalheVendaExterna(ResumoVendaExterna Resumo, string? Comprador, string? DescricaoCanal,
    DateOnly? Vencimento, Guid ResponsavelId, DateTimeOffset CriadaEmUtc, Guid? CanceladaPorId,
    DateTimeOffset? CanceladaEmUtc, string? MotivoCancelamento, IReadOnlyList<ItemVendaExternaDetalhe> Itens,
    IReadOnlyList<RecebimentoVendaExternaDetalhe> Recebimentos, IReadOnlyList<BaixaVendaExternaDetalhe> Baixas);
public sealed record ItemVendaExternaDetalhe(Guid Id, Guid ProdutoId, Guid VarianteId, string NomeProduto,
    string NomeVariante, int Quantidade,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long PrecoUnitarioCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long DescontoCentavos,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long SubtotalCentavos);
public sealed record RecebimentoVendaExternaDetalhe(Guid Id, DateOnly Data, string Meio,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long ValorCentavos,
    Guid ResponsavelId, DateTimeOffset RegistradoEmUtc);
public sealed record BaixaVendaExternaDetalhe(Guid ItemId, Guid LoteId, Guid MovimentoId, int Quantidade,
    Guid? MovimentoCancelamentoId);
public enum VendaExternaStatus { Sucesso, Repetida, Invalida, Conflito, Ausente }
public sealed record VendaExternaResultado(VendaExternaStatus Status, ResumoVendaExterna? Venda = null, string? Erro = null);

public interface IGerenciarVendasExternas
{
    Task<IReadOnlyList<ResumoVendaExterna>> ListarAsync(CanalVendaExterna? canal, SituacaoVendaExterna? situacao,
        int pagina, CancellationToken ct);
    Task<DetalheVendaExterna?> ConsultarAsync(Guid id, CancellationToken ct);
    Task<ResumoVendaExterna?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct);
    Task<VendaExternaResultado> CriarAsync(CriarVendaExternaInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<VendaExternaResultado> ReceberAsync(Guid id, ReceberVendaExternaInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<VendaExternaResultado> CancelarAsync(Guid id, CancelarVendaExternaInput input, Guid autor, Guid chave, CancellationToken ct);
}
