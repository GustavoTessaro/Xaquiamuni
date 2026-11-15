namespace Xaquiamuni.Aplicacao.Imagens;

public interface IProcessadorImagemProduto
{
    /// <summary>
    /// Validates and optimizes the input. The caller retains ownership of the input stream
    /// and owns/disposes the returned processed image.
    /// </summary>
    Task<ImagemProdutoProcessada> ProcessarAsync(
        Stream arquivo,
        CancellationToken cancellationToken = default);
}
