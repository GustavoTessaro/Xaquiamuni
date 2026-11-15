using System.Buffers.Binary;
using SkiaSharp;
using Xaquiamuni.Aplicacao.Imagens;

namespace Xaquiamuni.Infraestrutura.Imagens;

public sealed class ProcessadorImagemProduto : IProcessadorImagemProduto
{
    public const int TamanhoMaximoEntradaBytes = 5 * 1024 * 1024;
    public const int DimensaoMaximaEntradaPixels = 8000;
    public const long QuantidadeMaximaPixelsEntrada = 25_000_000;
    public const int MaiorLadoSaidaPixels = 1600;
    public const int QualidadeWebp = 85;

    public async Task<ImagemProdutoProcessada> ProcessarAsync(
        Stream arquivo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arquivo);
        if (!arquivo.CanRead)
        {
            throw new ArgumentException("O arquivo deve permitir leitura.", nameof(arquivo));
        }

        var bytes = await LerLimitadoAsync(arquivo, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (ContemControleAnimadoPng(bytes) || ContemQuadroAnimadoWebp(bytes))
        {
            throw Falha(
                ErroProcessamentoImagemProduto.ImagemAnimada,
                "Imagens animadas não são aceitas.");
        }

        using var dados = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(dados);
        if (codec is null)
        {
            throw Falha(ErroProcessamentoImagemProduto.ImagemInvalida, "O arquivo não contém uma imagem válida.");
        }

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            throw Falha(
                ErroProcessamentoImagemProduto.FormatoNaoSuportado,
                "Somente imagens JPEG, PNG e WebP são aceitas.");
        }

        if (codec.FrameCount > 1 || codec.RepetitionCount != 0)
        {
            throw Falha(
                ErroProcessamentoImagemProduto.ImagemAnimada,
                "Imagens animadas não são aceitas.");
        }

        var larguraOriginal = codec.Info.Width;
        var alturaOriginal = codec.Info.Height;
        if (larguraOriginal <= 0
            || alturaOriginal <= 0
            || larguraOriginal > DimensaoMaximaEntradaPixels
            || alturaOriginal > DimensaoMaximaEntradaPixels
            || (long)larguraOriginal * alturaOriginal > QuantidadeMaximaPixelsEntrada)
        {
            throw Falha(
                ErroProcessamentoImagemProduto.DimensoesExcessivas,
                "As dimensões da imagem excedem os limites permitidos.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var imagemInfo = new SKImageInfo(
            larguraOriginal,
            alturaOriginal,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var imagemDecodificada = new SKBitmap(imagemInfo);
        var resultadoDecodificacao = codec.GetPixels(imagemDecodificada.Info, imagemDecodificada.GetPixels());
        if (resultadoDecodificacao != SKCodecResult.Success || !imagemDecodificada.ReadyToDraw)
        {
            throw Falha(
                ErroProcessamentoImagemProduto.FalhaNaDecodificacao,
                "Não foi possível decodificar a imagem recebida.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var orientada = AplicarOrientacao(imagemDecodificada, codec.EncodedOrigin);
        var escala = Math.Min(1d, (double)MaiorLadoSaidaPixels / Math.Max(orientada.Width, orientada.Height));
        var larguraSaida = Math.Max(1, (int)Math.Round(orientada.Width * escala));
        var alturaSaida = Math.Max(1, (int)Math.Round(orientada.Height * escala));
        var informacaoSaida = new SKImageInfo(
            larguraSaida,
            alturaSaida,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var redimensionada = new SKBitmap(informacaoSaida);
        using (var canvas = new SKCanvas(redimensionada))
        {
            if (larguraSaida == orientada.Width && alturaSaida == orientada.Height)
            {
                canvas.DrawBitmap(
                    orientada,
                    new SKPoint(0, 0),
                    SKSamplingOptions.Default,
                    null);
            }
            else
            {
                canvas.DrawBitmap(
                    orientada,
                    new SKRect(0, 0, orientada.Width, orientada.Height),
                    new SKRect(0, 0, larguraSaida, alturaSaida),
                    new SKSamplingOptions(SKCubicResampler.Mitchell),
                    null);
            }

            canvas.Flush();
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var imagem = SKImage.FromBitmap(redimensionada);
        using var dadosWebp = imagem.Encode(SKEncodedImageFormat.Webp, QualidadeWebp);
        if (dadosWebp is null)
        {
            throw Falha(
                ErroProcessamentoImagemProduto.FalhaNaCodificacao,
                "Não foi possível gerar a imagem WebP otimizada.");
        }

        var conteudo = dadosWebp.ToArray();
        if (conteudo.Length == 0)
        {
            throw Falha(
                ErroProcessamentoImagemProduto.FalhaNaCodificacao,
                "O processador produziu um arquivo WebP vazio.");
        }

        return new ImagemProdutoProcessada(
            new MemoryStream(conteudo, writable: false),
            conteudo.LongLength,
            larguraSaida,
            alturaSaida);
    }

    private static async Task<byte[]> LerLimitadoAsync(Stream arquivo, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var bloco = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lidos = await arquivo.ReadAsync(bloco.AsMemory(), cancellationToken);
            if (lidos == 0)
            {
                break;
            }

            if (buffer.Length + lidos > TamanhoMaximoEntradaBytes)
            {
                throw Falha(
                    ErroProcessamentoImagemProduto.ArquivoMuitoGrande,
                    "O arquivo excede o limite de 5 MiB.");
            }

            await buffer.WriteAsync(bloco.AsMemory(0, lidos), cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw Falha(
                ErroProcessamentoImagemProduto.ImagemInvalida,
                "O arquivo de imagem está vazio.");
        }

        return buffer.ToArray();
    }

    private static SKBitmap AplicarOrientacao(SKBitmap origem, SKEncodedOrigin orientacao)
    {
        var inverteDimensoes = orientacao is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;
        var largura = inverteDimensoes ? origem.Height : origem.Width;
        var altura = inverteDimensoes ? origem.Width : origem.Height;
        var destino = new SKBitmap(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul));

        var matriz = orientacao switch
        {
            SKEncodedOrigin.TopRight =>
                new SKMatrix(-1, 0, origem.Width, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight =>
                new SKMatrix(-1, 0, origem.Width, 0, -1, origem.Height, 0, 0, 1),
            SKEncodedOrigin.BottomLeft =>
                new SKMatrix(1, 0, 0, 0, -1, origem.Height, 0, 0, 1),
            SKEncodedOrigin.LeftTop =>
                new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop =>
                new SKMatrix(0, -1, origem.Height, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom =>
                new SKMatrix(0, -1, origem.Height, -1, 0, origem.Width, 0, 0, 1),
            SKEncodedOrigin.LeftBottom =>
                new SKMatrix(0, 1, 0, -1, 0, origem.Width, 0, 0, 1),
            _ => SKMatrix.CreateIdentity()
        };

        using var canvas = new SKCanvas(destino);
        canvas.SetMatrix(matriz);
        canvas.DrawBitmap(
            origem,
            new SKRect(0, 0, origem.Width, origem.Height),
            SKSamplingOptions.Default,
            null);
        canvas.Flush();
        return destino;
    }

    private static bool ContemControleAnimadoPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.StartsWith(signature))
        {
            return false;
        }

        var offset = signature.Length;
        while (offset <= bytes.Length - 12)
        {
            var tamanho = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (tamanho > int.MaxValue || tamanho > (uint)(bytes.Length - offset - 12))
            {
                return false;
            }

            var tipoChunk = bytes.Slice(offset + 4, 4);
            if (tipoChunk.SequenceEqual("acTL"u8))
            {
                return true;
            }

            if (tipoChunk.SequenceEqual("IEND"u8))
            {
                return false;
            }

            offset += (int)tamanho + 12;
        }

        return false;
    }

    private static bool ContemQuadroAnimadoWebp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var offset = 12;
        while (offset <= bytes.Length - 8)
        {
            var tamanho = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (tamanho > int.MaxValue || tamanho > (uint)(bytes.Length - offset - 8))
            {
                return false;
            }

            var tipoChunk = bytes.Slice(offset, 4);
            if (tipoChunk.SequenceEqual("ANIM"u8) || tipoChunk.SequenceEqual("ANMF"u8))
            {
                return true;
            }

            offset += 8 + (int)tamanho + ((int)tamanho & 1);
        }

        return false;
    }

    private static ProcessamentoImagemProdutoException Falha(
        ErroProcessamentoImagemProduto erro,
        string mensagem) =>
        new(erro, mensagem);
}
