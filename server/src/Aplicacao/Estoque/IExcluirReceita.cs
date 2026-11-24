namespace Xaquiamuni.Aplicacao.Estoque;

public interface IExcluirReceita
{
    Task<(int Status, string? Codigo)> ExecutarAsync(Guid receita, Guid? revisao, Guid token,
        Guid autor, Guid chave, CancellationToken ct);
}
