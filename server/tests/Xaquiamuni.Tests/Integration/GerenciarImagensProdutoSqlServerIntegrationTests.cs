using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo.Imagens;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Imagens;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class GerenciarImagensProdutoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task GerenciarImagens_PersisteAbreRemoveEExcluiArquivoNoFilesystemTemporario()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var sandbox = new TemporaryImageDirectory();
        await using var context = database.CreateVerificationContext();
        var category = Categoria.Criar("Imagens", null, 0);
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var defaultVariant = ProdutoVariante.Criar("Padrão", "UN", 20m, padrao: true);
        var secondVariant = ProdutoVariante.Criar("Outra opção", "KIT", 25m);
        var product = Produto.Criar("Produto com fotos", null, category, [defaultVariant, secondVariant]);
        context.Produtos.Add(product);
        await context.SaveChangesAsync();

        var storage = sandbox.CreateStorage();
        var manager = new GerenciarImagensProduto(
            new EfProdutoRepository(context),
            new ProcessadorImagemProduto(),
            storage,
            new NoOpImageLogger());
        using var source = new MemoryStream(CreatePng());

        var added = await manager.AdicionarAsync(
            product.Id,
            new AdicionarImagemProdutoInput(source, "Foto de detalhe.png", [defaultVariant.Id, secondVariant.Id]));

        Assert.Equal(ProdutoImagemOperacaoStatus.Success, added.Status);
        var imageDto = Assert.IsType<ProdutoImagemDto>(added.Imagem);
        Assert.True(imageDto.Principal);
        Assert.Equal("image/webp", imageDto.TipoConteudo);
        Assert.Equal(
            new[] { defaultVariant.Id, secondVariant.Id }.Order(),
            imageDto.VarianteIds);

        await using var verification = database.CreateVerificationContext();
        var persisted = await verification.ProdutoImagens
            .Include(image => image.Variantes)
            .SingleAsync(image => image.Id == imageDto.Id);
        Assert.Equal(imageDto.TamanhoBytes, persisted.TamanhoBytes);
        Assert.Equal(imageDto.LarguraPixels, persisted.LarguraPixels);
        Assert.Equal(imageDto.AlturaPixels, persisted.AlturaPixels);
        Assert.Equal(2, persisted.Variantes.Count);
        Assert.True(await storage.ExisteAsync(persisted.ChaveArmazenamento));

        using var opened = await manager.AbrirConteudoAsync(product.Id, imageDto.Id);
        Assert.Equal(ProdutoImagemOperacaoStatus.Success, opened.Status);
        var webpBytes = await ReadAllAsync(opened.Conteudo!);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(webpBytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(webpBytes, 8, 4));

        var removed = await manager.RemoverAsync(product.Id, imageDto.Id);
        Assert.Equal(ProdutoImagemOperacaoStatus.Success, removed.Status);
        Assert.Empty(removed.Imagens);
        Assert.False(await storage.ExisteAsync(persisted.ChaveArmazenamento));
        Assert.Empty(await verification.ProdutoImagens
            .Where(image => image.ProdutoId == product.Id)
            .ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task UploadsConcorrentes_NaoUltrapassamCincoImagensENaoDeixamArquivosOrfaos()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var sandbox = new TemporaryImageDirectory();
        Guid productId;
        await using (var context = database.CreateVerificationContext())
        {
            var category = Categoria.Criar("Concorrência de imagens", null, 0);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var variant = ProdutoVariante.Criar("Padrão", "UN", 20m, padrao: true);
            var product = Produto.Criar("Produto no limite", null, category, [variant]);
            for (var index = 0; index < ProdutoImagem.QuantidadeMaximaPorProduto - 1; index++)
            {
                product.AdicionarImagem(
                    ChaveArmazenamentoImagemProduto.Gerar(),
                    $"legacy-{index}.webp",
                    "image/webp",
                    4,
                    20,
                    20);
            }

            context.Produtos.Add(product);
            await context.SaveChangesAsync();
            productId = product.Id;
        }

        var processor = new TwoPartyBarrierProcessor();
        var storage = sandbox.CreateStorage();
        using var firstInput = new MemoryStream([1]);
        using var secondInput = new MemoryStream([2]);
        var firstUpload = UploadInOwnContextAsync(
            database,
            productId,
            firstInput,
            processor,
            storage);
        var secondUpload = UploadInOwnContextAsync(
            database,
            productId,
            secondInput,
            processor,
            storage);
        var results = await Task.WhenAll(firstUpload, secondUpload);

        Assert.Single(results, result => result.Status == ProdutoImagemOperacaoStatus.Success);
        Assert.Single(results, result => result.Status == ProdutoImagemOperacaoStatus.Conflict);
        await using var verification = database.CreateVerificationContext();
        var persistedImages = await verification.ProdutoImagens
            .Where(image => image.ProdutoId == productId)
            .ToListAsync();
        Assert.Equal(ProdutoImagem.QuantidadeMaximaPorProduto, persistedImages.Count);
        Assert.Single(persistedImages, image => image.Principal);
        Assert.Single(Directory.GetFiles(sandbox.ImagesDirectory, "*.webp"));
    }

    [SqlServerIntegrationFact]
    public async Task TrocasDePrincipalEPublicacaoConcorrentesComRemocao_PreservamIntegridade()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var sandbox = new TemporaryImageDirectory();
        var storage = sandbox.CreateStorage();
        Guid productId;
        Guid firstImageId;
        Guid secondImageId;

        await using (var context = database.CreateVerificationContext())
        {
            var category = Categoria.Criar("Concorrência de publicação", null, 0);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();
            var variant = ProdutoVariante.Criar("Padrão", "UN", 20m, padrao: true);
            var product = Produto.Criar("Produto com duas imagens", null, category, [variant]);
            var firstImage = product.AdicionarImagem(
                ChaveArmazenamentoImagemProduto.Gerar(),
                "first.webp",
                "image/webp",
                3,
                32,
                24);
            var secondImage = product.AdicionarImagem(
                ChaveArmazenamentoImagemProduto.Gerar(),
                "second.webp",
                "image/webp",
                3,
                32,
                24);
            context.Produtos.Add(product);
            await context.SaveChangesAsync();
            productId = product.Id;
            firstImageId = firstImage.Id;
            secondImageId = secondImage.Id;
            await storage.ArmazenarAsync(firstImage.ChaveArmazenamento, new MemoryStream([1, 2, 3]));
            await storage.ArmazenarAsync(secondImage.ChaveArmazenamento, new MemoryStream([4, 5, 6]));
        }

        await RunTogetherAsync(
            () => RunPrincipalChangeAsync(database, storage, productId, firstImageId),
            () => RunPrincipalChangeAsync(database, storage, productId, secondImageId));

        await using (var verification = database.CreateVerificationContext())
        {
            var product = await verification.Produtos
                .Include(item => item.Imagens)
                .SingleAsync(item => item.Id == productId);
            Assert.Single(product.Imagens, image => image.Principal);
            var imageToKeep = product.Imagens.Single(image => image.Principal);
            var imageToRemove = product.Imagens.Single(image => image.Id != imageToKeep.Id);
            var removal = await RunImageRemovalAsync(database, storage, productId, imageToRemove.Id);
            Assert.Equal(ProdutoImagemOperacaoStatus.Success, removal.Status);
            firstImageId = imageToKeep.Id;
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishTask = WaitForReleaseAsync(
            start.Task,
            () => RunPublicationAsync(database, productId, published: true));
        var removeTask = WaitForReleaseAsync(
            start.Task,
            () => RunImageRemovalAsync(database, storage, productId, firstImageId));
        start.SetResult();
        await Task.WhenAll(publishTask, removeTask);

        await using var finalVerification = database.CreateVerificationContext();
        var finalProduct = await finalVerification.Produtos
            .Include(item => item.Imagens)
            .SingleAsync(item => item.Id == productId);
        Assert.False(finalProduct.Publicado && finalProduct.Imagens.Count == 0);
        if (finalProduct.Publicado)
        {
            Assert.Single(finalProduct.Imagens, image => image.Principal);
        }
    }

    private static async Task<ProdutoImagemOperacaoResult> UploadInOwnContextAsync(
        SqlServerTestDatabase database,
        Guid productId,
        Stream input,
        IProcessadorImagemProduto processor,
        ArmazenamentoLocalImagemProduto storage)
    {
        await using var context = database.CreateVerificationContext();
        var manager = new GerenciarImagensProduto(
            new EfProdutoRepository(context),
            processor,
            storage,
            new NoOpImageLogger());
        return await manager.AdicionarAsync(
            productId,
            new AdicionarImagemProdutoInput(input, "concurrent.webp"));
    }

    private static async Task<ProdutoImagemOperacaoResult> RunPrincipalChangeAsync(
        SqlServerTestDatabase database,
        ArmazenamentoLocalImagemProduto storage,
        Guid productId,
        Guid imageId)
    {
        await using var context = database.CreateVerificationContext();
        var manager = CreateManager(context, storage);
        return await manager.DefinirPrincipalAsync(productId, imageId);
    }

    private static async Task<ProdutoImagemOperacaoResult> RunImageRemovalAsync(
        SqlServerTestDatabase database,
        ArmazenamentoLocalImagemProduto storage,
        Guid productId,
        Guid imageId)
    {
        await using var context = database.CreateVerificationContext();
        var manager = CreateManager(context, storage);
        return await manager.RemoverAsync(productId, imageId);
    }

    private static async Task<ProdutoResult> RunPublicationAsync(
        SqlServerTestDatabase database,
        Guid productId,
        bool published)
    {
        await using var context = database.CreateVerificationContext();
        var manager = new GerenciarProdutos(new EfProdutoRepository(context));
        return await manager.AlterarPublicacaoAsync(productId, published);
    }

    private static GerenciarImagensProduto CreateManager(
        XaquiamuniDbContext context,
        ArmazenamentoLocalImagemProduto storage) =>
        new(
            new EfProdutoRepository(context),
            new ProcessadorImagemProduto(),
            storage,
            new NoOpImageLogger());

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(8, 6);
        bitmap.Erase(SKColors.CadetBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private static async Task<T[]> RunTogetherAsync<T>(
        Func<Task<T>> first,
        Func<Task<T>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = WaitForReleaseAsync(start.Task, first);
        var secondTask = WaitForReleaseAsync(start.Task, second);
        start.SetResult();
        return await Task.WhenAll(firstTask, secondTask);
    }

    private static async Task<T> WaitForReleaseAsync<T>(
        Task release,
        Func<Task<T>> operation)
    {
        await release;
        return await operation();
    }

    private sealed class TemporaryImageDirectory : IDisposable
    {
        private readonly string _contentRoot = Path.Combine(
            Path.GetTempPath(),
            $"xaquiamuni-image-app-{Guid.NewGuid():N}");
        private readonly string _storageRoot = Path.Combine(
            Path.GetTempPath(),
            $"xaquiamuni-image-store-{Guid.NewGuid():N}");

        public string ImagesDirectory => Path.Combine(_storageRoot, "product-images");

        public ArmazenamentoLocalImagemProduto CreateStorage() =>
            new(
                Options.Create(new ArmazenamentoLocalImagemProdutoOptions
                {
                    RootPath = _storageRoot
                }),
                new TestHostEnvironment(_contentRoot));

        public void Dispose()
        {
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }

            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Xaquiamuni.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NoOpImageLogger : IProdutoImagemOperacaoLogger
    {
        public void RegistrarFalhaDeArmazenamento(
            Guid produtoId,
            Guid? imagemId,
            string? chave,
            string etapa,
            Exception? exception)
        {
        }
    }

    private sealed class TwoPartyBarrierProcessor : IProcessadorImagemProduto
    {
        private readonly TaskCompletionSource _bothCalls = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public async Task<ImagemProdutoProcessada> ProcessarAsync(
            Stream arquivo,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                _bothCalls.TrySetResult();
            }

            await _bothCalls.Task.WaitAsync(cancellationToken);
            return new ImagemProdutoProcessada(
                new MemoryStream([1, 2, 3], writable: false),
                3,
                32,
                24);
        }
    }
}
