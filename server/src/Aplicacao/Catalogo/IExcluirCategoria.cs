namespace Xaquiamuni.Aplicacao.Catalogo;

public enum ExclusaoCategoriaStatus { Excluida = 1, Ausente, ComProdutos, VersaoDesatualizada, ChaveEmUso, ConflitoConcorrencia }

public interface IExcluirCategoria
{
    Task<ExclusaoCategoriaStatus> ExecutarAsync(Guid id, Guid versao, Guid autor, Guid chave, CancellationToken ct);
}

public sealed class CategoriaVersaoException : Exception;
