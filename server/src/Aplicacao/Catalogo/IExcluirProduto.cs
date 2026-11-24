using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface IExcluirProduto
{
    Task<(int Status, string? Codigo)> ExecutarAsync(Guid produtoId, string versao, Guid autor, Guid chave,
        Func<Produto, string> calcularVersao, CancellationToken ct);
}
