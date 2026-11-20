using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ValidadeComponenteRecebido(Guid ComponenteId, DateOnly DataValidade);
public sealed record ReceberCompraMateriaPrimaInput(Guid Versao, IReadOnlyList<ValidadeComponenteRecebido>? Validades, bool SemControleValidade = false);
public sealed record ReceberCompraMateriaPrimaResult(CompraMateriaPrimaStatus Status,
    CompraMateriaPrima? Compra = null, IReadOnlyList<EntradaRecebimentoCompra>? Entradas = null, string? Erro = null);

public interface IReceberCompraMateriaPrima
{
    Task<ReceberCompraMateriaPrimaResult> ReceberAsync(Guid id, Guid autor, Guid chave,
        ReceberCompraMateriaPrimaInput input, CancellationToken ct);
}
