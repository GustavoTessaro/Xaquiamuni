namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public sealed class ProdutoImagemConteudoResult : IDisposable
{
    private ProdutoImagemConteudoResult(
        ProdutoImagemOperacaoStatus status,
        ProdutoImagemDto? imagem,
        Stream? conteudo,
        string? mensagem)
    {
        Status = status;
        Imagem = imagem;
        Conteudo = conteudo;
        Mensagem = mensagem;
    }

    public ProdutoImagemOperacaoStatus Status { get; }
    public ProdutoImagemDto? Imagem { get; }

    /// <summary>
    /// Caller-owned stream returned only on success. Dispose this result to close it.
    /// </summary>
    public Stream? Conteudo { get; }

    public string? Mensagem { get; }

    public static ProdutoImagemConteudoResult Success(ProdutoImagemDto imagem, Stream conteudo) =>
        new(ProdutoImagemOperacaoStatus.Success, imagem, conteudo, null);

    public static ProdutoImagemConteudoResult ProductNotFound() =>
        Empty(ProdutoImagemOperacaoStatus.ProductNotFound);

    public static ProdutoImagemConteudoResult ImageNotFound() =>
        Empty(ProdutoImagemOperacaoStatus.ImageNotFound);

    public static ProdutoImagemConteudoResult ValidationFailed(string mensagem) =>
        Empty(ProdutoImagemOperacaoStatus.ValidationFailed, mensagem);

    public static ProdutoImagemConteudoResult StoredFileMissing() =>
        Empty(
            ProdutoImagemOperacaoStatus.StoredFileMissing,
            "Os metadados existem, mas o arquivo não foi encontrado no armazenamento.");

    public static ProdutoImagemConteudoResult StorageUnavailable() =>
        Empty(ProdutoImagemOperacaoStatus.StorageUnavailable);

    public void Dispose() => Conteudo?.Dispose();

    private static ProdutoImagemConteudoResult Empty(
        ProdutoImagemOperacaoStatus status,
        string? mensagem = null) =>
        new(status, null, null, mensagem);
}
