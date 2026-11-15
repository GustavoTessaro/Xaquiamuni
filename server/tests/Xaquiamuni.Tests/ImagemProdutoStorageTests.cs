using System.Buffers.Binary;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Imagens;

namespace Xaquiamuni.Tests;

public sealed class ImagemProdutoStorageTests
{
    [Fact]
    public async Task ArmazenamentoLocal_GravaLeAbreEExcluiDeFormaIdempotente()
    {
        using var sandbox = new ImageStorageSandbox();
        var storage = sandbox.CreateStorage();
        var key = ChaveArmazenamentoImagemProduto.Gerar();
        var content = new MemoryStream([1, 2, 3, 4]);

        await storage.ArmazenarAsync(key, content);
        Assert.True(content.CanRead);
        Assert.True(await storage.ExisteAsync(key));
        using (var stored = await storage.AbrirLeituraAsync(key))
        {
            Assert.NotNull(stored);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await ReadAllAsync(stored!));
        }

        await storage.ExcluirAsync(key);
        await storage.ExcluirAsync(key);
        Assert.False(await storage.ExisteAsync(key));
        Assert.Null(await storage.AbrirLeituraAsync(key));
    }

    [Fact]
    public async Task ArmazenamentoLocal_NaoSobrescreveChaveExistente()
    {
        using var sandbox = new ImageStorageSandbox();
        var storage = sandbox.CreateStorage();
        var key = ChaveArmazenamentoImagemProduto.Gerar();
        await storage.ArmazenarAsync(key, new MemoryStream([1]));

        await Assert.ThrowsAsync<IOException>(() =>
            storage.ArmazenarAsync(key, new MemoryStream([2])));

        using var stored = await storage.AbrirLeituraAsync(key);
        Assert.Equal(new byte[] { 1 }, await ReadAllAsync(stored!));
        Assert.Empty(Directory.GetFiles(sandbox.ImagesDirectory, "*.tmp"));
    }

    [Theory]
    [InlineData("../outside.webp")]
    [InlineData("C:\\outside.webp")]
    [InlineData("product-images/../outside.webp")]
    [InlineData("product-images/not-a-guid.webp")]
    public async Task ArmazenamentoLocal_RejeitaChavesArbitrarias(string key)
    {
        using var sandbox = new ImageStorageSandbox();
        var storage = sandbox.CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.ExcluirAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.ArmazenarAsync(key, new MemoryStream([1])));
    }

    [Fact]
    public async Task ArmazenamentoLocal_LimpaTemporarioQuandoGravacaoFalha()
    {
        using var sandbox = new ImageStorageSandbox();
        var storage = sandbox.CreateStorage();
        var source = new ThrowAfterFirstReadStream();

        await Assert.ThrowsAsync<IOException>(() =>
            storage.ArmazenarAsync(ChaveArmazenamentoImagemProduto.Gerar(), source));

        Assert.Empty(Directory.GetFiles(sandbox.ImagesDirectory, "*.tmp"));
    }

    [Fact]
    public async Task ArmazenamentoLocal_LimpaTemporarioQuandoOperacaoECancelada()
    {
        using var sandbox = new ImageStorageSandbox();
        var storage = sandbox.CreateStorage();
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelOnFirstReadStream(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.ArmazenarAsync(
                ChaveArmazenamentoImagemProduto.Gerar(),
                source,
                cancellation.Token));

        Assert.Empty(Directory.GetFiles(sandbox.ImagesDirectory, "*.tmp"));
    }

    [Fact]
    public void ArmazenamentoLocal_RejeitaDiretorioDentroDoWebRoot()
    {
        using var sandbox = new ImageStorageSandbox();
        var environment = new TestHostEnvironment(sandbox.ContentRoot);
        var options = Microsoft.Extensions.Options.Options.Create(
            new ArmazenamentoLocalImagemProdutoOptions
            {
                RootPath = Path.Combine(sandbox.ContentRoot, "wwwroot", "images")
            });

        Assert.Throws<InvalidOperationException>(() =>
            new ArmazenamentoLocalImagemProduto(options, environment));
    }

    [Fact]
    public void ArmazenamentoLocal_RejeitaDiretorioEmOutraPastaDoRepositorio()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"xaquiamuni-repo-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(repoRoot, "server", "src", "Api");
        var storageRoot = Path.Combine(repoRoot, "docs", "images");
        Directory.CreateDirectory(contentRoot);
        File.WriteAllText(Path.Combine(repoRoot, ".git"), "gitdir: synthetic");

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(
                new ArmazenamentoLocalImagemProdutoOptions { RootPath = storageRoot });
            Assert.Throws<InvalidOperationException>(() =>
                new ArmazenamentoLocalImagemProduto(options, new TestHostEnvironment(contentRoot)));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Processador_AceitaJpegPngEWebp(SKEncodedImageFormat format)
    {
        var bytes = CreateImage(16, 8, format);
        using var input = new MemoryStream(bytes);

        using var result = await new ProcessadorImagemProduto().ProcessarAsync(input);

        Assert.Equal(ImagemProdutoProcessada.TipoConteudoWebp, result.TipoConteudo);
        Assert.Equal(result.TamanhoBytes, result.Conteudo.Length);
        Assert.Equal(16, result.LarguraPixels);
        Assert.Equal(8, result.AlturaPixels);
        Assert.True(input.CanRead);
        AssertWebp(result);
    }

    [Fact]
    public async Task Processador_RejeitaImagemCorrompidaSemConsiderarSeuNome()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("not an image named photo.jpg"));

        var exception = await Assert.ThrowsAsync<ProcessamentoImagemProdutoException>(() =>
            new ProcessadorImagemProduto().ProcessarAsync(input));

        Assert.Equal(ErroProcessamentoImagemProduto.ImagemInvalida, exception.Erro);
    }

    [Fact]
    public async Task Processador_RejeitaArquivoAcimaDoLimiteMesmoSemLengthConfiavel()
    {
        using var input = new LengthLyingStream(new byte[ProcessadorImagemProduto.TamanhoMaximoEntradaBytes + 1]);

        var exception = await Assert.ThrowsAsync<ProcessamentoImagemProdutoException>(() =>
            new ProcessadorImagemProduto().ProcessarAsync(input));

        Assert.Equal(ErroProcessamentoImagemProduto.ArquivoMuitoGrande, exception.Erro);
    }

    [Fact]
    public async Task Processador_RejeitaDimensaoExcessivaAntesDaDecodificacao()
    {
        using var input = new MemoryStream(CreatePngHeader(
            ProcessadorImagemProduto.DimensaoMaximaEntradaPixels + 1,
            1));

        var exception = await Assert.ThrowsAsync<ProcessamentoImagemProdutoException>(() =>
            new ProcessadorImagemProduto().ProcessarAsync(input));

        Assert.Equal(ErroProcessamentoImagemProduto.DimensoesExcessivas, exception.Erro);
    }

    [Fact]
    public async Task Processador_RejeitaQuantidadeExcessivaDePixelsAntesDaDecodificacao()
    {
        using var input = new MemoryStream(CreatePngHeader(5000, 5001));

        var exception = await Assert.ThrowsAsync<ProcessamentoImagemProdutoException>(() =>
            new ProcessadorImagemProduto().ProcessarAsync(input));

        Assert.Equal(ErroProcessamentoImagemProduto.DimensoesExcessivas, exception.Erro);
    }

    [Fact]
    public async Task Processador_RejeitaWebpAnimado()
    {
        var bytes = CreateAnimatedWebp();
        using var input = new MemoryStream(bytes);

        var exception = await Assert.ThrowsAsync<ProcessamentoImagemProdutoException>(() =>
            new ProcessadorImagemProduto().ProcessarAsync(input));

        Assert.Equal(ErroProcessamentoImagemProduto.ImagemAnimada, exception.Erro);
    }

    [Fact]
    public async Task Processador_RejeitaPngComControleDeAnimacao()
    {
        var png = CreateImage(2, 2, SKEncodedImageFormat.Png);
        var controlChunk = CreatePngChunk("acTL", [0, 0, 0, 2, 0, 0, 0, 0]);
        var animatedPng = new byte[png.Length + controlChunk.Length];
        png.AsSpan(0, 33).CopyTo(animatedPng);
        controlChunk.CopyTo(animatedPng, 33);
        png.AsSpan(33).CopyTo(animatedPng.AsSpan(33 + controlChunk.Length));
        using var input = new MemoryStream(animatedPng);

        var exception = await Assert.ThrowsAsync<ProcessamentoImagemProdutoException>(() =>
            new ProcessadorImagemProduto().ProcessarAsync(input));

        Assert.Equal(ErroProcessamentoImagemProduto.ImagemAnimada, exception.Erro);
    }

    [Fact]
    public async Task Processador_RedimensionaSemAmpliarEAplicaOrientacaoExif()
    {
        var largeBytes = CreateImage(2400, 1200, SKEncodedImageFormat.Png);
        using var largeInput = new MemoryStream(largeBytes);
        using var largeResult = await new ProcessadorImagemProduto().ProcessarAsync(largeInput);
        Assert.Equal((1600, 800), (largeResult.LarguraPixels, largeResult.AlturaPixels));

        var smallBytes = CreateImage(12, 8, SKEncodedImageFormat.Png);
        using var smallInput = new MemoryStream(smallBytes);
        using var smallResult = await new ProcessadorImagemProduto().ProcessarAsync(smallInput);
        Assert.Equal((12, 8), (smallResult.LarguraPixels, smallResult.AlturaPixels));

        var orientedBytes = AddExifOrientation(
            CreateImage(3, 2, SKEncodedImageFormat.Jpeg),
            orientation: 6);
        using var orientedInput = new MemoryStream(orientedBytes);
        using var orientedResult = await new ProcessadorImagemProduto().ProcessarAsync(orientedInput);
        Assert.Equal((2, 3), (orientedResult.LarguraPixels, orientedResult.AlturaPixels));
        Assert.DoesNotContain(Encoding.ASCII.GetBytes("Exif\0\0"), ReadProcessedBytes(orientedResult));
        AssertWebp(orientedResult);
    }

    [Fact]
    public async Task Processador_PreservaTransparenciaENaoPersisteMetadadosExif()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(0, 0, new SKColor(20, 40, 60, 0));
        bitmap.SetPixel(1, 0, new SKColor(200, 30, 40, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var input = new MemoryStream(png!.ToArray());
        using var result = await new ProcessadorImagemProduto().ProcessarAsync(input);

        using var decoded = SKBitmap.Decode(ReadProcessedBytes(result));
        Assert.NotNull(decoded);
        Assert.Equal(0, decoded!.GetPixel(0, 0).Alpha);
        Assert.DoesNotContain(Encoding.ASCII.GetBytes("Exif\0\0"), ReadProcessedBytes(result));
        AssertWebp(result);
    }

    [Fact]
    public async Task AddInfrastructureServices_RegistraEResolveStorageEProcessador()
    {
        using var sandbox = new ImageStorageSandbox();
        Directory.CreateDirectory(sandbox.ContentRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = sandbox.ContentRoot,
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ArmazenamentoLocalImagemProdutoOptions.SectionName}:RootPath"] = sandbox.StorageRoot
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        using var app = builder.Build();

        Assert.IsType<ArmazenamentoLocalImagemProduto>(
            app.Services.GetRequiredService<IArmazenamentoImagemProduto>());
        Assert.IsType<ProcessadorImagemProduto>(
            app.Services.GetRequiredService<IProcessadorImagemProduto>());
    }

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data!.ToArray();
    }

    private static byte[] CreateAnimatedWebp()
    {
        using var first = new SKBitmap(new SKImageInfo(2, 2));
        using var second = new SKBitmap(new SKImageInfo(2, 2));
        first.Erase(SKColors.Red);
        second.Erase(SKColors.Blue);
        var frames = new[]
        {
            new SKWebpEncoderFrame(first, TimeSpan.FromMilliseconds(100)),
            new SKWebpEncoderFrame(second, TimeSpan.FromMilliseconds(100))
        };
        using var data = SKWebpEncoder.EncodeAnimated(frames, SKWebpEncoderOptions.Default);
        return data!.ToArray();
    }

    private static byte[] AddExifOrientation(byte[] jpeg, ushort orientation)
    {
        var payload = new byte[32];
        Encoding.ASCII.GetBytes("Exif\0\0").CopyTo(payload, 0);
        payload[6] = (byte)'I';
        payload[7] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(10, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(18, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24, 2), orientation);

        var segment = new byte[payload.Length + 4];
        segment[0] = 0xff;
        segment[1] = 0xe1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2, 2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);

        var result = new byte[jpeg.Length + segment.Length];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        segment.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + segment.Length));
        return result;
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var png = CreateImage(1, 1, SKEncodedImageFormat.Png);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), height);
        BinaryPrimitives.WriteUInt32BigEndian(
            png.AsSpan(29, 4),
            Crc32(png.AsSpan(12, 17).ToArray()));
        return png;
    }

    private static byte[] CreatePngChunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(
            chunk.AsSpan(chunk.Length - 4, 4),
            Crc32(typeBytes.Concat(data).ToArray()));
        return chunk;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void AssertWebp(ImagemProdutoProcessada result)
    {
        var bytes = ReadProcessedBytes(result);
        Assert.True(bytes.Length > 12);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal((result.LarguraPixels, result.AlturaPixels), (decoded!.Width, decoded.Height));
    }

    private static byte[] ReadProcessedBytes(ImagemProdutoProcessada result)
    {
        var stream = result.Conteudo;
        var position = stream.Position;
        stream.Position = 0;
        using var output = new MemoryStream();
        stream.CopyTo(output);
        stream.Position = position;
        return output.ToArray();
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private sealed class ImageStorageSandbox : IDisposable
    {
        public ImageStorageSandbox()
        {
            var identifier = Guid.NewGuid().ToString("N");
            var temp = Path.GetTempPath();
            ContentRoot = Path.Combine(temp, $"xaquiamuni-app-{identifier}");
            StorageRoot = Path.Combine(temp, $"xaquiamuni-images-{identifier}");
            ImagesDirectory = Path.Combine(StorageRoot, "product-images");
        }

        public string ContentRoot { get; }
        public string StorageRoot { get; }
        public string ImagesDirectory { get; }

        public ArmazenamentoLocalImagemProduto CreateStorage() =>
            new(
                Microsoft.Extensions.Options.Options.Create(new ArmazenamentoLocalImagemProdutoOptions
                {
                    RootPath = StorageRoot
                }),
                new TestHostEnvironment(ContentRoot));

        public void Dispose()
        {
            if (Directory.Exists(ContentRoot))
            {
                Directory.Delete(ContentRoot, recursive: true);
            }

            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Xaquiamuni.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class LengthLyingStream(byte[] content) : MemoryStream(content)
    {
        public override long Length => 1;
        public override bool CanSeek => false;
    }

    private sealed class ThrowAfterFirstReadStream : Stream
    {
        private int _calls;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_calls++ == 0)
            {
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }

            throw new IOException("Synthetic read failure.");
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelOnFirstReadStream(CancellationTokenSource cancellation) : Stream
    {
        private int _calls;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_calls++ == 0)
            {
                buffer.Span[0] = 1;
                cancellation.Cancel();
                return ValueTask.FromResult(1);
            }

            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
