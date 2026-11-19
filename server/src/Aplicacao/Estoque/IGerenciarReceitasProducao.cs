using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public sealed record ItemReceitaInput(Guid InsumoId,
    string? Quantidade, string? UnidadeBase);
public sealed record ReceitaInput(Guid ProdutoVarianteId, string? Nome, int Rendimento,
    IReadOnlyList<ItemReceitaInput>? Itens, Guid? Token);
public enum ReceitaStatus { Sucesso, Invalida, Ausente, Conflito }
public sealed record ReceitaResult(ReceitaStatus Status, ReceitaProducao? Receita = null, string? Erro = null);

public interface IGerenciarReceitasProducao
{
    Task<IReadOnlyList<ReceitaProducao>> ListarAsync(CancellationToken ct);
    Task<ReceitaProducao?> ObterAsync(Guid id, CancellationToken ct);
    Task<ReceitaResult> SalvarAsync(Guid? id, ReceitaInput input, Guid autor, CancellationToken ct);
    Task<ReceitaResult> AlterarStatusAsync(Guid id, bool ativa, Guid token, Guid autor, CancellationToken ct);
}
