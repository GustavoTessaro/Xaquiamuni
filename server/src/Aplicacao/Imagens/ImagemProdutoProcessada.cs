namespace Xaquiamuni.Aplicacao.Imagens;

public sealed class ImagemProdutoProcessada : IDisposable
{
    public const string TipoConteudoWebp = "image/webp";

    public ImagemProdutoProcessada(Stream conteudo, long tamanhoBytes, int larguraPixels, int alturaPixels)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        if (!conteudo.CanRead)
        {
            throw new ArgumentException("O conteúdo processado deve permitir leitura.", nameof(conteudo));
        }

        if (tamanhoBytes <= 0 || larguraPixels <= 0 || alturaPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tamanhoBytes),
                "O tamanho e as dimensões da imagem processada devem ser positivos.");
        }

        Conteudo = conteudo;
        TamanhoBytes = tamanhoBytes;
        LarguraPixels = larguraPixels;
        AlturaPixels = alturaPixels;
    }

    public Stream Conteudo { get; }
    public string TipoConteudo => TipoConteudoWebp;
    public long TamanhoBytes { get; }
    public int LarguraPixels { get; }
    public int AlturaPixels { get; }

    public void Dispose() => Conteudo.Dispose();
}
