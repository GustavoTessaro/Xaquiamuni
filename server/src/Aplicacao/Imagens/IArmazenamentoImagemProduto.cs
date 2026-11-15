namespace Xaquiamuni.Aplicacao.Imagens;

public interface IArmazenamentoImagemProduto
{
    /// <summary>
    /// Persists the stream under a system-generated key without taking ownership of the stream.
    /// A duplicate key fails with <see cref="IOException"/>; cancellation and storage errors propagate.
    /// </summary>
    Task ArmazenarAsync(string chave, Stream conteudo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a caller-owned readable stream, or null when the key does not exist.
    /// </summary>
    Task<Stream?> AbrirLeituraAsync(string chave, CancellationToken cancellationToken = default);

    Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the object if it exists. Repeated deletion succeeds; cancellation and storage errors propagate.
    /// </summary>
    Task ExcluirAsync(string chave, CancellationToken cancellationToken = default);
}
