using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record DadosFornecedor(string? Nome, string? Telefone, string? Email, string? Observacoes);
public enum FornecedorStatus { Sucesso, Invalido, NaoEncontrado, NomeDuplicado }
public sealed record ResultadoFornecedor(FornecedorStatus Status, Fornecedor? Fornecedor = null, string? Erro = null);

public interface IGerenciarFornecedores
{
    Task<IReadOnlyList<Fornecedor>> ListarAsync(string? busca, bool? ativo, CancellationToken ct);
    Task<Fornecedor?> ConsultarAsync(Guid id, CancellationToken ct);
    Task<ResultadoFornecedor> CadastrarAsync(DadosFornecedor dados, CancellationToken ct);
    Task<ResultadoFornecedor> EditarAsync(Guid id, DadosFornecedor dados, CancellationToken ct);
    Task<ResultadoFornecedor> AlterarStatusAsync(Guid id, bool ativo, CancellationToken ct);
}
