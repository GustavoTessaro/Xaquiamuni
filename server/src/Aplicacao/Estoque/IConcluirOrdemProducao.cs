using System.Text.Json.Serialization;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ConsumoProducaoInput(Guid InsumoId, string? UnidadeBase, string? Quantidade);
public sealed record ConcluirProducaoInput(Guid Versao, int QuantidadeProduzida,
    IReadOnlyList<ConsumoProducaoInput>? Consumos, DateOnly? DataFabricacao, DateOnly? DataValidade);
public sealed record ConsumoProducaoResultado(Guid InsumoId, string Nome, string UnidadeBase,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Consumida,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Liberada);
public sealed record ConclusaoProducao(Guid OrdemId, string Situacao, Guid Versao, int QuantidadeProduzida,
    Guid LoteId, string CodigoLote, Guid MovimentoEntradaId, DateOnly DataFabricacao, DateOnly DataValidade,
    Guid ResponsavelId, DateTimeOffset ConcluidaEmUtc, IReadOnlyList<ConsumoProducaoResultado> Consumos);
public interface IConcluirOrdemProducao
{
    Task<OperacaoProducaoResult> ConcluirAsync(Guid id, Guid autor, Guid chave, ConcluirProducaoInput input, CancellationToken ct);
    Task<ConclusaoProducao> ConsultarConclusaoAsync(Guid id, CancellationToken ct);
}
