using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record InicioProducaoInput(Guid ReceitaId, Guid ReceitaVersaoId, int Quantidade);
public sealed record MaterialProducao(Guid InsumoId, string Nome, string Unidade,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Necessario,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Utilizavel,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Reservado,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Livre,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Falta);
public sealed record PreviaProducao(Guid ReceitaVersaoId, int Versao, IReadOnlyList<MaterialProducao> Materiais);
public sealed record ResumoOrdemProducao(Guid Id, Guid ProdutoId, string Produto, string Variante, Guid ReceitaId,
    string Receita, int Versao, int Quantidade, string Situacao, DateTimeOffset IniciadaEmUtc, DateTimeOffset? EncerradaEmUtc, Guid VersaoOrdem);
public sealed record MaterialOrdemProducao(Guid InsumoId, string Nome, string Unidade,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Necessario,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Reservado,
    bool ReservaInelegivel);
public sealed record DetalheOrdemProducao(ResumoOrdemProducao Resumo, IReadOnlyList<MaterialOrdemProducao> Materiais,
    Guid VarianteId, ConclusaoProducao? Conclusao);
public enum OperacaoProducaoStatus { Sucesso, Repetida, Invalida, Ausente, Conflito }
public sealed record OperacaoProducaoResult(OperacaoProducaoStatus Status, Guid? OrdemId = null,
    PreviaProducao? Previa = null, string? Erro = null, bool RejeicaoDefinitiva = false);

public interface IGerenciarOrdensProducao
{
    Task<IReadOnlyList<ResumoOrdemProducao>> ListarAsync(SituacaoOrdemProducao? situacao, Guid? produtoId, Guid? receitaId, CancellationToken ct);
    Task<DetalheOrdemProducao?> ConsultarAsync(Guid id, CancellationToken ct);
    Task<Guid?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct);
    Task<OperacaoProducaoResult> PreverAsync(Guid receitaId, int quantidade, CancellationToken ct);
    Task<OperacaoProducaoResult> IniciarAsync(InicioProducaoInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<OperacaoProducaoResult> CancelarAsync(Guid id, Guid autor, Guid chave, CancellationToken ct);
}
