using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task LimpezaIndividual_RecuperaIntencoesAoReiniciarHost()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        var temporario = Directory.CreateTempSubdirectory("xaquiamuni-image-restart-");
        try
        {
            await using var db = banco.CreateVerificationContext();
            var categoria = Categoria.Criar("Reinício", null, 0);
            var produto = Produto.CriarSimples("Produto com duas fotos", null, categoria, "UN", 19);
            db.Produtos.Add(produto);
            await db.SaveChangesAsync();
            var chaves = new List<string>();
            await using (var host = new HostE2ECompra(banco, temporario.FullName, temporario.FullName,
                relogioAplicacao: new RelogioAnterior()))
            {
                await using var escopo = host.Services.CreateAsyncScope();
                var manager = escopo.ServiceProvider.GetRequiredService<IGerenciarImagensProduto>();
                var storage = escopo.ServiceProvider.GetRequiredService<IArmazenamentoImagemProduto>();
                for (var contador = 0; contador < 2; contador++)
                {
                    using var origem = new MemoryStream(CreatePng());
                    var imagem = Assert.IsType<ProdutoImagemDto>((await manager.AdicionarAsync(produto.Id,
                        new AdicionarImagemProdutoInput(origem, $"foto-{contador}.png"))).Imagem);
                    var chave = await db.ProdutoImagens.Where(i => i.Id == imagem.Id)
                        .Select(i => i.ChaveArmazenamento).SingleAsync();
                    chaves.Add(chave);
                    Assert.Equal(ProdutoImagemOperacaoStatus.CleanupPending, (await manager.RemoverAsync(produto.Id, imagem.Id)).Status);
                    Assert.True(await storage.ExisteAsync(chave));
                }
                Assert.Equal(2, await ContarLimpezasAsync(db));
                // Reproduz a interrupção entre a remoção física e o commit da fila.
                await storage.ExcluirAsync(chaves[0]);
            }

            await using var novoHost = new HostE2ECompra(banco, temporario.FullName, temporario.FullName);
            await using var novoEscopo = novoHost.Services.CreateAsyncScope();
            var novoStorage = novoEscopo.ServiceProvider.GetRequiredService<IArmazenamentoImagemProduto>();
            using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await ContarLimpezasAsync(db, tempo.Token) != 0)
                await Task.Delay(100, tempo.Token);
            foreach (var chave in chaves)
                Assert.False(await novoStorage.ExisteAsync(chave));
            Assert.False(await db.ProdutoImagens.AnyAsync());
        }
        finally
        {
            temporario.Delete(recursive: true);
        }
    }

    private static Task<int> ContarLimpezasAsync(XaquiamuniDbContext db, CancellationToken ct = default) =>
        db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM LimpezasImagensProdutos").SingleAsync(ct);

    private sealed class RelogioAnterior : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    [SqlServerIntegrationFact]
    public async Task LimpezaIndividual_RollbackPreservaMetadadosEArquivo()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        using var pasta = new TemporaryImageDirectory();
        var storage = pasta.CreateStorage();
        await using var db = banco.CreateVerificationContext();
        var produto = Produto.CriarSimples("Rollback", null, Categoria.Criar("Rollback", null, 0), "UN", 10);
        var chave = ChaveArmazenamentoImagemProduto.Gerar();
        var imagem = produto.AdicionarImagem(chave, "foto.webp", "image/webp", 3, 32, 32);
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        using var arquivo = new MemoryStream([1, 2, 3]);
        await storage.ArmazenarAsync(chave, arquivo);
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(new FalhaDepoisDeSalvarImagem()).Options;
        await using var falha = new XaquiamuniDbContext(options);
        var manager = new GerenciarImagensProduto(new EfProdutoRepository(falha), new ProcessadorImagemProduto(),
            storage, new NoOpImageLogger());
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RemoverAsync(produto.Id, imagem.Id));
        Assert.Equal(0, await ContarLimpezasAsync(db));
        Assert.True(await db.ProdutoImagens.AnyAsync(i => i.Id == imagem.Id));
        Assert.True(await storage.ExisteAsync(chave));
    }

    private sealed class FalhaDepoisDeSalvarImagem : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha de teste antes do commit.");
    }

    [SqlServerIntegrationFact]
    public async Task LimpezaIndividual_DoisProcessadoresTratamUmaIntencao()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        using var pasta = new TemporaryImageDirectory();
        var storage = pasta.CreateStorage();
        await using var db = banco.CreateVerificationContext();
        var chave = ChaveArmazenamentoImagemProduto.Gerar();
        using var arquivo = new MemoryStream([1, 2, 3]);
        await storage.ArmazenarAsync(chave, arquivo);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO LimpezasImagensProdutos (Id, Chave, ProximaTentativaUtc)
            VALUES ({Guid.NewGuid()}, {chave}, {DateTimeOffset.UtcNow.AddMinutes(-1)})
            """);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var bloqueante = new ArmazenamentoComBarreira(storage);
        await using var primeiro = banco.CreateVerificationContext();
        var interceptador = new SinalDeLeituraDaFila();
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(interceptador).Options;
        await using var segundo = new XaquiamuniDbContext(options);
        Task Processar(XaquiamuniDbContext contexto) =>
            new LimpezaImagensProduto(contexto, bloqueante, TimeProvider.System,
                NullLogger<LimpezaImagensProduto>.Instance).ProcessarAsync(limite.Token);
        var tarefa1 = Processar(primeiro);
        await bloqueante.Iniciou.Task.WaitAsync(limite.Token);
        var tarefa2 = Processar(segundo);
        try
        {
            await interceptador.Iniciou.Task.WaitAsync(limite.Token);
            Assert.False(tarefa2.IsCompleted);
        }
        finally
        {
            bloqueante.Prosseguir.TrySetResult();
        }
        await Task.WhenAll(tarefa1, tarefa2);
        Assert.Equal(1, bloqueante.Exclusoes);
        Assert.Equal(0, await ContarLimpezasAsync(db));
        Assert.False(await storage.ExisteAsync(chave));
    }

    private sealed class SinalDeLeituraDaFila : DbCommandInterceptor
    {
        public TaskCompletionSource Iniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal))
                Iniciou.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ArmazenamentoComBarreira(IArmazenamentoImagemProduto interno) : IArmazenamentoImagemProduto
    {
        public int Exclusoes { get; private set; }
        public TaskCompletionSource Iniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Prosseguir { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExcluirAsync(string chave, CancellationToken ct = default)
        {
            Exclusoes++;
            Iniciou.TrySetResult();
            await Prosseguir.Task.WaitAsync(ct);
            await interno.ExcluirAsync(chave, ct);
        }

        public Task<bool> ExisteAsync(string chave, CancellationToken ct = default) => interno.ExisteAsync(chave, ct);
        public Task<Stream?> AbrirLeituraAsync(string chave, CancellationToken ct = default) => interno.AbrirLeituraAsync(chave, ct);
        public Task ArmazenarAsync(string chave, Stream conteudo, CancellationToken ct = default) =>
            interno.ArmazenarAsync(chave, conteudo, ct);
    }

    [SqlServerIntegrationFact]
    public async Task LimpezaIndividual_ValidaChavesEPreservaArquivoForaDaRaiz()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        using var pasta = new TemporaryImageDirectory();
        Directory.CreateDirectory(pasta.ImagesDirectory);
        var protegido = Path.GetFullPath(Path.Combine(pasta.ImagesDirectory, "..", "fallback.webp"));
        await File.WriteAllBytesAsync(protegido, [1, 2, 3]);
        var storage = pasta.CreateStorage();
        await using var db = banco.CreateVerificationContext();
        foreach (var chave in new[] { "product-images/../fallback.webp", protegido, "product-images\\fallback.webp" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => storage.ExcluirAsync(chave));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO LimpezasImagensProdutos (Id, Chave, ProximaTentativaUtc)
                VALUES ({Guid.NewGuid()}, {chave}, {DateTimeOffset.UtcNow.AddMinutes(-1)})
                """);
        }
        await new LimpezaImagensProduto(db, storage, TimeProvider.System,
            NullLogger<LimpezaImagensProduto>.Instance).ProcessarAsync(default);
        Assert.Equal(0, await ContarLimpezasAsync(db));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(protegido));

        using var pastaComLink = new TemporaryImageDirectory();
        var chaveValida = ChaveArmazenamentoImagemProduto.Gerar();
        var destinoDoLink = Path.Combine(pasta.ImagesDirectory, chaveValida[15..]);
        await File.WriteAllBytesAsync(destinoDoLink, [4, 5, 6]);
        Directory.CreateDirectory(Path.GetDirectoryName(pastaComLink.ImagesDirectory)!);
        Directory.CreateSymbolicLink(pastaComLink.ImagesDirectory, pasta.ImagesDirectory);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => pastaComLink.CreateStorage().ExcluirAsync(chaveValida));
            Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(destinoDoLink));
        }
        finally
        {
            Directory.Delete(pastaComLink.ImagesDirectory);
        }
    }

    [SqlServerIntegrationFact]
    public async Task LimpezaIndividual_NovaIntencaoAoRemoverUltimaReferenciaDaChave()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        using var pasta = new TemporaryImageDirectory();
        var storage = pasta.CreateStorage();
        await using var db = banco.CreateVerificationContext();
        var categoria = Categoria.Criar("Compartilhamento", null, 0);
        var primeiro = Produto.CriarSimples("Primeiro", null, categoria, "UN", 10);
        var ultimo = Produto.CriarSimples("Último", null, categoria, "UN", 10);
        var chave = ChaveArmazenamentoImagemProduto.Gerar();
        var alias = "product-images/" + chave[15..^5].ToUpperInvariant() + ".webp";
        var imagemPrimeira = primeiro.AdicionarImagem(alias, "primeira.webp", "image/webp", 3, 32, 32);
        var imagemUltima = ultimo.AdicionarImagem(chave, "ultima.webp", "image/webp", 3, 32, 32);
        db.Produtos.AddRange(primeiro, ultimo);
        await db.SaveChangesAsync();
        using var arquivo = new MemoryStream([1, 2, 3]);
        await storage.ArmazenarAsync(chave, arquivo);
        var manager = new GerenciarImagensProduto(new EfProdutoRepository(db), new ProcessadorImagemProduto(),
            storage, new NoOpImageLogger());
        await using var workerDb = banco.CreateVerificationContext();
        var worker = new LimpezaImagensProduto(workerDb, storage, TimeProvider.System,
            NullLogger<LimpezaImagensProduto>.Instance);
        Assert.Equal(ProdutoImagemOperacaoStatus.CleanupPending, (await manager.RemoverAsync(primeiro.Id, imagemPrimeira.Id)).Status);
        await worker.ProcessarAsync(default);
        Assert.True(await storage.ExisteAsync(chave));
        Assert.Equal(0, await ContarLimpezasAsync(db));
        Assert.Equal(ProdutoImagemOperacaoStatus.CleanupPending, (await manager.RemoverAsync(ultimo.Id, imagemUltima.Id)).Status);
        Assert.Equal(1, await ContarLimpezasAsync(db));
        await worker.ProcessarAsync(default);
        Assert.False(await storage.ExisteAsync(chave));
        Assert.Equal(0, await ContarLimpezasAsync(db));
    }

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
        Assert.Equal(ProdutoImagemOperacaoStatus.CleanupPending, removed.Status);
        Assert.Empty(removed.Imagens);
        Assert.True(await storage.ExisteAsync(persisted.ChaveArmazenamento));
        Assert.Equal(1, await verification.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM LimpezasImagensProdutos").SingleAsync());
        Assert.Equal(ProdutoImagemOperacaoStatus.ImageNotFound, (await manager.RemoverAsync(product.Id, imageDto.Id)).Status);
        await new LimpezaImagensProduto(verification, storage, TimeProvider.System,
            NullLogger<LimpezaImagensProduto>.Instance).ProcessarAsync(default);
        Assert.False(await storage.ExisteAsync(persisted.ChaveArmazenamento));
        Assert.Equal(0, await verification.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM LimpezasImagensProdutos").SingleAsync());
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
            Assert.Equal(ProdutoImagemOperacaoStatus.CleanupPending, removal.Status);
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
