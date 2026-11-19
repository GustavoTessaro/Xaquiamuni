using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ComponenteCompraInput(Guid InsumoId, Guid? ApresentacaoId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantidade, Guid? SnapshotId);
public sealed record LinhaCompraInput(string? Kit,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantidade,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? ValorTotal,
    IReadOnlyList<ComponenteCompraInput>? Componentes);
public sealed record CompraMateriaPrimaInput(Guid FornecedorId, DateOnly DataCompra,
    IReadOnlyList<LinhaCompraInput>? Linhas, Guid? Versao);
public enum CompraMateriaPrimaStatus { Sucesso, Repetida, Invalida, Ausente, Conflito }
public sealed record CompraMateriaPrimaResult(CompraMateriaPrimaStatus Status, CompraMateriaPrima? Compra = null,
    string? Erro = null);

public interface IGerenciarComprasMateriaPrima
{
    Task<IReadOnlyList<CompraMateriaPrima>> ListarAsync(string? busca, SituacaoCompraMateriaPrima? situacao, CancellationToken ct);
    Task<CompraMateriaPrima?> ConsultarAsync(Guid id, CancellationToken ct);
    Task<CompraMateriaPrima?> ConsultarCriacaoAsync(Guid autor, Guid chave, CancellationToken ct);
    Task<CompraMateriaPrimaResult> CriarAsync(CompraMateriaPrimaInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<CompraMateriaPrimaResult> EditarAsync(Guid id, CompraMateriaPrimaInput input, Guid autor, CancellationToken ct);
    Task<CompraMateriaPrimaResult> CancelarAsync(Guid id, Guid versao, Guid autor, CancellationToken ct);
}
