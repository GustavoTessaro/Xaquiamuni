using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public interface IExcluirVariante
{
    Task<(int Status, string? Codigo)> ExecutarAsync(Guid produtoId, Guid varianteId, string versao,
        Guid autor, Guid chave, Func<Produto, string> calcularVersao, CancellationToken ct);
}
