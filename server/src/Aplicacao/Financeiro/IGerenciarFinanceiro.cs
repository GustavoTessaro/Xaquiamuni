using System.Text.Json.Serialization;
using Xaquiamuni.Dominio.Financeiro;

namespace Xaquiamuni.Aplicacao.Financeiro;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CriarLancamentoInput(string Tipo, string? Categoria, string? Descricao, DateOnly Data,
    string Meio, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ValorCentavos,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Contraparte = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Observacoes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CriarLancamentoPendenteInput(string Tipo, string? Categoria, string? Descricao, DateOnly? Vencimento,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ValorCentavos,
    string? Contraparte = null, string? Observacoes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuitarLancamentoInput(DateOnly Data, string Meio);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelarLancamentoInput(string? Motivo);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PagarFornecedorInput(DateOnly Data, string Meio, string? Referencia,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ValorCentavos);

public sealed record LancamentoDto(Guid Id, string Tipo, string Categoria, string Descricao, string ValorCentavos,
    DateOnly? Data, string? Meio, Guid ResponsavelId, DateTimeOffset CriadoEmUtc,
    Guid? CanceladoPorId, DateTimeOffset? CanceladoEmUtc, string? MotivoCancelamento,
    DateOnly? Vencimento = null, string? Contraparte = null, string? Observacoes = null,
    Guid? QuitadoPorId = null, DateTimeOffset? QuitadoEmUtc = null)
{
    public string Situacao => CanceladoEmUtc.HasValue ? "Cancelado" : Data.HasValue ? "Realizado" : "Pendente";
}

public sealed record PagamentoFornecedorDto(Guid Id, Guid CompraId, string ValorCentavos, DateOnly Data, string Meio,
    string? Referencia, Guid ResponsavelId, DateTimeOffset CriadoEmUtc);

public sealed record CompraFinanceiraDto(Guid CompraId, Guid FornecedorId, string NomeFornecedor, DateOnly DataCompra,
    string SituacaoCompra, string SituacaoFinanceira, string ComposicaoValor,
    string? TotalCentavos, string PagoCentavos, string? SaldoCentavos);

public sealed record TentativaFinanceiraDto(string Operacao, Guid RecursoId, object Resultado);

public enum StatusFinanceiro { Sucesso, Repetida, Invalida, Ausente, Conflito }
public sealed record ResultadoFinanceiro<T>(StatusFinanceiro Status, T? Dados = default, string? Erro = null);

public interface IGerenciarFinanceiro
{
    Task<ResumoFinanceiroDto> ResumirAsync(DateOnly? inicio, DateOnly? fim, CancellationToken ct);
    Task<PaginaContasEmAberto> ListarContasEmAbertoAsync(FiltroContasEmAberto filtro, CancellationToken ct);
    Task<PaginaMovimentacoesFinanceiras> ListarMovimentacoesAsync(FiltroMovimentacoesFinanceiras filtro, CancellationToken ct);
    Task<TentativaFinanceiraDto?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct);
    Task<IReadOnlyList<LancamentoDto>> ListarAsync(int pagina, CancellationToken ct,
        SituacaoLancamento? situacao = null, TipoLancamento? tipo = null);
    Task<LancamentoDto?> ConsultarAsync(Guid id, CancellationToken ct);
    Task<CompraFinanceiraDto?> ConsultarCompraAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PagamentoFornecedorDto>?> ListarPagamentosAsync(Guid compra, int pagina, CancellationToken ct);
    Task<ResultadoFinanceiro<LancamentoDto>> CriarAsync(CriarLancamentoInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoFinanceiro<LancamentoDto>> CriarPendenteAsync(CriarLancamentoPendenteInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoFinanceiro<LancamentoDto>> QuitarAsync(Guid id, QuitarLancamentoInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoFinanceiro<LancamentoDto>> CancelarAsync(Guid id, CancelarLancamentoInput input, Guid autor, Guid chave, CancellationToken ct);
    Task<ResultadoFinanceiro<PagamentoFornecedorDto>> PagarAsync(Guid compra, PagarFornecedorInput input, Guid autor, Guid chave, CancellationToken ct);
}
