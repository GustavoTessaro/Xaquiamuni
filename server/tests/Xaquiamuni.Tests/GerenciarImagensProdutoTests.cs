using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo.Imagens;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Dominio.Entidades;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Imagens;

namespace Xaquiamuni.Tests;

public sealed class GerenciarImagensProdutoTests
{
    [Fact]
    public async Task Adicionar_PersisteMetadadosProcessadosEAssociaMultiplasVariantes()
    {
        var fixture = new Fixture();
        using var source = new MemoryStream([9, 8, 7]);

        var result = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(
                source,
                "nome original.webp",
                [fixture.DefaultVariant.Id, fixture.SecondVariant.Id]));

        Assert.Equal(ProdutoImagemOperacaoStatus.Success, result.Status);
        var image = Assert.IsType<ProdutoImagemDto>(result.Imagem);
        Assert.Equal("nome original.webp", image.NomeOriginal);
        Assert.Equal("image/webp", image.TipoConteudo);
        Assert.Equal(7, image.TamanhoBytes);
        Assert.Equal((640, 480), (image.LarguraPixels, image.AlturaPixels));
        Assert.Equal(
            new[] { fixture.DefaultVariant.Id, fixture.SecondVariant.Id }.Order(),
            image.VarianteIds);
        Assert.True(image.Principal);
        Assert.True(source.CanRead);
        Assert.Single(fixture.Product.Imagens);
        Assert.Single(fixture.Storage.Objects);
        Assert.StartsWith("product-images/", Assert.Single(fixture.Storage.Objects.Keys));
        Assert.DoesNotContain("nome original.webp", Assert.Single(fixture.Storage.Objects.Keys));
    }

    [Fact]
    public async Task Adicionar_RejeitaProdutoInexistenteNomeInvalidoEVariantesDesconhecidasAntesDeProcessar()
    {
        var fixture = new Fixture();
        using var source = new MemoryStream([1]);

        var missingProduct = await fixture.Manager.AdicionarAsync(
            Guid.NewGuid(),
            new AdicionarImagemProdutoInput(source, "photo.webp"));
        var invalidName = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, new string('x', ProdutoImagem.NomeOriginalMaxLength + 1)));
        var missingVariant = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "photo.webp", [Guid.NewGuid()]));

        Assert.Equal(ProdutoImagemOperacaoStatus.ProductNotFound, missingProduct.Status);
        Assert.Equal(ProdutoImagemOperacaoStatus.ValidationFailed, invalidName.Status);
        Assert.Equal(ProdutoImagemOperacaoStatus.VariantNotFound, missingVariant.Status);
        Assert.Equal(0, fixture.Processor.Calls);
        Assert.Empty(fixture.Storage.Objects);
    }

    [Fact]
    public async Task Adicionar_RejeitaSextaImagemAntesDoProcessamento()
    {
        var fixture = new Fixture();
        for (var index = 0; index < ProdutoImagem.QuantidadeMaximaPorProduto; index++)
        {
            fixture.Product.AdicionarImagem(
                ChaveArmazenamentoImagemProduto.Gerar(),
                $"image-{index}.webp",
                "image/webp",
                7,
                640,
                480);
        }

        using var source = new MemoryStream([1]);
        var result = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "sixth.webp"));

        Assert.Equal(ProdutoImagemOperacaoStatus.Conflict, result.Status);
        Assert.Equal(0, fixture.Processor.Calls);
        Assert.Equal(ProdutoImagem.QuantidadeMaximaPorProduto, fixture.Product.Imagens.Count);
    }

    [Fact]
    public async Task Adicionar_ProcessamentoOuArmazenamentoComFalhaNaoCriaMetadados()
    {
        var fixture = new Fixture();
        using var source = new MemoryStream([1]);
        fixture.Processor.Failure = new ProcessamentoImagemProdutoException(
            ErroProcessamentoImagemProduto.ImagemInvalida,
            "Imagem inválida.");

        var processFailure = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "invalid.webp"));

        fixture.Processor.Failure = null;
        fixture.Storage.FailStore = true;
        var storageFailure = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "valid.webp"));

        Assert.Equal(ProdutoImagemOperacaoStatus.InvalidImage, processFailure.Status);
        Assert.Equal(ProdutoImagemOperacaoStatus.StorageUnavailable, storageFailure.Status);
        Assert.Empty(fixture.Product.Imagens);
    }

    [Fact]
    public async Task Adicionar_CompensaFalhaDePersistenciaAposRollbackConfirmado()
    {
        var fixture = new Fixture { RepositoryMode = RepositoryMode.RolledBackConflict };
        using var source = new MemoryStream([1]);

        var result = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "valid.webp"));

        Assert.Equal(ProdutoImagemOperacaoStatus.Conflict, result.Status);
        Assert.Empty(fixture.Product.Imagens);
        Assert.Empty(fixture.Storage.Objects);
    }

    [Fact]
    public async Task Adicionar_RegistraCompensacaoPendenteSeArquivoNaoPuderSerRemovido()
    {
        var fixture = new Fixture { RepositoryMode = RepositoryMode.RolledBackConflict };
        fixture.Storage.FailDelete = true;
        using var source = new MemoryStream([1]);

        var result = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "valid.webp"));

        Assert.Equal(ProdutoImagemOperacaoStatus.CompensationPending, result.Status);
        Assert.Single(fixture.Storage.Objects);
        Assert.Empty(fixture.Product.Imagens);
        Assert.Contains(fixture.Logger.Events, entry => entry.Stage == "compensation");
    }

    [Fact]
    public async Task Adicionar_NaoRemoveArquivoQuandoResultadoDoCommitEIncerto()
    {
        var fixture = new Fixture { RepositoryMode = RepositoryMode.UnknownAfterMutation };
        using var source = new MemoryStream([1]);

        var result = await fixture.Manager.AdicionarAsync(
            fixture.Product.Id,
            new AdicionarImagemProdutoInput(source, "valid.webp"));

        Assert.Equal(ProdutoImagemOperacaoStatus.CommitOutcomeUnknown, result.Status);
        Assert.Single(fixture.Product.Imagens);
        Assert.Single(fixture.Storage.Objects);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
        Assert.Contains(
            fixture.Logger.Events,
            entry => entry.Stage == "persist-outcome-unknown" && entry.Key is not null);
    }

    [Fact]
    public async Task Adicionar_PropagaFalhaSqlDepoisDeCompensarRollbackConfirmado()
    {
        var fixture = new Fixture { RepositoryMode = RepositoryMode.RolledBackFailure };
        using var source = new MemoryStream([1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Manager.AdicionarAsync(
                fixture.Product.Id,
                new AdicionarImagemProdutoInput(source, "valid.webp")));

        Assert.Empty(fixture.Product.Imagens);
        Assert.Empty(fixture.Storage.Objects);
    }

    [Fact]
    public async Task ListarConsultarEAbrirConteudo_OrdenaESeparaMetadadosDeArquivoAusente()
    {
        var fixture = new Fixture();
        var first = fixture.AddExistingImage("first.webp", order: 2);
        var second = fixture.AddExistingImage("second.webp", order: 1);
        fixture.Storage.Objects[first.ChaveArmazenamento] = [1, 2, 3];
        fixture.Storage.Objects[second.ChaveArmazenamento] = [4, 5];

        var list = await fixture.Manager.ListarAsync(fixture.Product.Id);
        var queried = await fixture.Manager.ConsultarAsync(fixture.Product.Id, first.Id);
        using var content = await fixture.Manager.AbrirConteudoAsync(fixture.Product.Id, first.Id);
        fixture.Storage.FailRead = true;
        using var unavailableStorage = await fixture.Manager.AbrirConteudoAsync(fixture.Product.Id, first.Id);
        fixture.Storage.FailRead = false;
        fixture.Storage.Objects.Remove(first.ChaveArmazenamento);
        using var missingFile = await fixture.Manager.AbrirConteudoAsync(fixture.Product.Id, first.Id);

        Assert.Equal(ProdutoImagemOperacaoStatus.Success, list.Status);
        Assert.Equal(new[] { second.Id, first.Id }, list.Imagens.Select(image => image.Id));
        Assert.Equal(ProdutoImagemOperacaoStatus.Success, queried.Status);
        Assert.Equal(ProdutoImagemOperacaoStatus.Success, content.Status);
        Assert.Equal(new byte[] { 1, 2, 3 }, await ReadAllAsync(content.Conteudo!));
        Assert.Equal(ProdutoImagemOperacaoStatus.StorageUnavailable, unavailableStorage.Status);
        Assert.Equal(ProdutoImagemOperacaoStatus.StoredFileMissing, missingFile.Status);
        Assert.Null(missingFile.Conteudo);
    }

    [Fact]
    public async Task Operacoes_DePrincipalOrdemEVinculosUsamOAgregado()
    {
        var fixture = new Fixture();
        var first = fixture.AddExistingImage("first.webp", order: 1, principal: true);
        var second = fixture.AddExistingImage("second.webp", order: 2);

        var principal = await fixture.Manager.DefinirPrincipalAsync(fixture.Product.Id, second.Id);
        var order = await fixture.Manager.ReordenarAsync(fixture.Product.Id, [second.Id, first.Id]);
        var linked = await fixture.Manager.AssociarVarianteAsync(
            fixture.Product.Id,
            first.Id,
            fixture.SecondVariant.Id);
        var unlinked = await fixture.Manager.DesassociarVarianteAsync(
            fixture.Product.Id,
            first.Id,
            fixture.SecondVariant.Id);
        var duplicateOrder = await fixture.Manager.ReordenarAsync(
            fixture.Product.Id,
            [first.Id, first.Id]);
        var wrongVariant = await fixture.Manager.AssociarVarianteAsync(
            fixture.Product.Id,
            first.Id,
            Guid.NewGuid());

        Assert.Equal(ProdutoImagemOperacaoStatus.Success, principal.Status);
        Assert.Contains(principal.Imagens, image => image.Id == second.Id && image.Principal);
        Assert.Equal(new[] { second.Id, first.Id }, order.Imagens.Select(image => image.Id));
        Assert.Equal(ProdutoImagemOperacaoStatus.Success, linked.Status);
        Assert.Contains(linked.Imagens.Single(image => image.Id == first.Id).VarianteIds,
            id => id == fixture.SecondVariant.Id);
        Assert.Equal(ProdutoImagemOperacaoStatus.Success, unlinked.Status);
        Assert.DoesNotContain(
            unlinked.Imagens.Single(image => image.Id == first.Id).VarianteIds,
            id => id == fixture.SecondVariant.Id);
        Assert.Equal(ProdutoImagemOperacaoStatus.ValidationFailed, duplicateOrder.Status);
        Assert.Equal(ProdutoImagemOperacaoStatus.VariantNotFound, wrongVariant.Status);
    }

    [Fact]
    public async Task Remover_PromovePrincipalEProtegeUltimaImagemPublicada()
    {
        var fixture = new Fixture();
        var main = fixture.AddExistingImage("main.webp", order: 1, principal: true);
        var other = fixture.AddExistingImage("other.webp", order: 2);
        fixture.Storage.Objects[main.ChaveArmazenamento] = [1];
        fixture.Storage.Objects[other.ChaveArmazenamento] = [2];
        fixture.Product.Publicar();

        var removedMain = await fixture.Manager.RemoverAsync(fixture.Product.Id, main.Id);
        var blockedLast = await fixture.Manager.RemoverAsync(fixture.Product.Id, other.Id);

        Assert.Equal(ProdutoImagemOperacaoStatus.Success, removedMain.Status);
        Assert.Single(removedMain.Imagens, image => image.Id == other.Id && image.Principal);
        Assert.False(fixture.Storage.Objects.ContainsKey(main.ChaveArmazenamento));
        Assert.Equal(ProdutoImagemOperacaoStatus.Conflict, blockedLast.Status);
        Assert.Single(fixture.Product.Imagens);
        Assert.True(fixture.Storage.Objects.ContainsKey(other.ChaveArmazenamento));
    }

    [Fact]
    public async Task Remover_NaoAfirmaExclusaoFisicaQuandoArmazenamentoFalha()
    {
        var fixture = new Fixture();
        var image = fixture.AddExistingImage("image.webp", order: 1, principal: true);
        fixture.Storage.Objects[image.ChaveArmazenamento] = [1];
        fixture.Storage.FailDelete = true;

        var result = await fixture.Manager.RemoverAsync(fixture.Product.Id, image.Id);

        Assert.Equal(ProdutoImagemOperacaoStatus.CleanupPending, result.Status);
        Assert.Empty(fixture.Product.Imagens);
        Assert.True(fixture.Storage.Objects.ContainsKey(image.ChaveArmazenamento));
        Assert.Contains("metadados foram removidos", result.Mensagem);
        Assert.Contains(
            fixture.Logger.Events,
            entry => entry.Stage == "delete-after-metadata-removal"
                && entry.Key == image.ChaveArmazenamento);
    }

    [Fact]
    public void AddInfrastructureServices_RegistraCasoDeUsoDeImagensComoScoped()
    {
        var applicationRoot = Path.Combine(Path.GetTempPath(), $"xaquiamuni-app-{Guid.NewGuid():N}");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"xaquiamuni-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(applicationRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = applicationRoot,
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ArmazenamentoLocalImagemProdutoOptions.SectionName}:RootPath"] = storageRoot
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        try
        {
            using var app = builder.Build();
            using var scope = app.Services.CreateScope();
            Assert.IsType<GerenciarImagensProduto>(
                scope.ServiceProvider.GetRequiredService<IGerenciarImagensProduto>());
            Assert.Equal(
                ServiceLifetime.Scoped,
                Assert.Single(builder.Services, descriptor =>
                    descriptor.ServiceType == typeof(IGerenciarImagensProduto)).Lifetime);
        }
        finally
        {
            Directory.Delete(applicationRoot, recursive: true);
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private sealed class Fixture
    {
        private readonly Categoria _category;
        private readonly Dictionary<Guid, int> _ordens = [];

        public Fixture()
        {
            _category = Categoria.Criar("Categoria", null, 0);
            DefaultVariant = ProdutoVariante.Criar("Padrão", "UN", 20m, padrao: true);
            SecondVariant = ProdutoVariante.Criar("Outra opção", "UN", 25m);
            Product = Produto.Criar("Produto", null, _category, [DefaultVariant, SecondVariant]);
            Repository.Product = Product;
            Manager = new GerenciarImagensProduto(
                Repository,
                Processor,
                Storage,
                Logger);
        }

        public Produto Product { get; }
        public ProdutoVariante DefaultVariant { get; }
        public ProdutoVariante SecondVariant { get; }
        public MemoryRepository Repository { get; } = new();
        public FakeProcessor Processor { get; } = new();
        public FakeStorage Storage { get; } = new();
        public RecordingLogger Logger { get; } = new();
        public GerenciarImagensProduto Manager { get; }
        public RepositoryMode RepositoryMode
        {
            get => Repository.Mode;
            init => Repository.Mode = value;
        }

        public ProdutoImagem AddExistingImage(
            string name,
            int order,
            bool principal = false)
        {
            var image = Product.AdicionarImagem(
                ChaveArmazenamentoImagemProduto.Gerar(),
                name,
                "image/webp",
                7,
                640,
                480);
            _ordens[image.Id] = order;
            Product.ReordenarImagens(
                Product.Imagens
                    .OrderBy(item => _ordens[item.Id])
                    .Select(item => item.Id));
            if (principal)
            {
                Product.DefinirImagemPrincipal(image.Id);
            }

            return image;
        }
    }

    private enum RepositoryMode
    {
        Normal,
        RolledBackConflict,
        RolledBackFailure,
        UnknownAfterMutation
    }

    private sealed class MemoryRepository : IProdutoRepository
    {
        public Produto? Product { get; set; }
        public RepositoryMode Mode { get; set; }

        public Task<IReadOnlyList<Produto>> ListarAsync(
            ProdutoFiltro filtro,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Produto>>(Product is null ? [] : [Product]);

        public Task<Produto?> ConsultarAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Product?.Id == id ? Product : null);

        public async Task<ProdutoResult> ExecutarMutacaoAsync(
            Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
            CancellationToken cancellationToken = default)
        {
            var execution = await ExecutarMutacaoComResultadoAsync(operacao, cancellationToken);
            if (execution.Failure is not null)
            {
                throw execution.Failure;
            }

            return execution.Result!;
        }

        public async Task<ProdutoMutationExecutionResult> ExecutarMutacaoComResultadoAsync(
            Func<IProdutoMutationSession, CancellationToken, Task<ProdutoResult>> operacao,
            CancellationToken cancellationToken = default)
        {
            if (Mode == RepositoryMode.RolledBackConflict)
            {
                return ProdutoMutationExecutionResult.RolledBack(
                    ProdutoResult.Conflict("Injected rollback."));
            }

            if (Mode == RepositoryMode.RolledBackFailure)
            {
                return ProdutoMutationExecutionResult.Failed(
                    ProdutoMutationPersistenceState.RolledBack,
                    new InvalidOperationException("Injected SQL failure."));
            }

            var product = Product!;
            var result = await operacao(new MemorySession(product), cancellationToken);
            if (Mode == RepositoryMode.UnknownAfterMutation)
            {
                return ProdutoMutationExecutionResult.Failed(
                    ProdutoMutationPersistenceState.Unknown,
                    new IOException("Injected uncertain commit."));
            }

            return result.Status == ProdutoStatus.Success
                ? ProdutoMutationExecutionResult.Committed(result)
                : ProdutoMutationExecutionResult.RolledBack(result);
        }
    }

    private sealed class MemorySession(Produto product) : IProdutoMutationSession
    {
        public Task<Categoria?> BuscarCategoriaAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Categoria?>(null);

        public Task<Produto?> BuscarProdutoAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(product.Id == id ? product : null);

        public void Adicionar(Produto produto) => throw new NotSupportedException();
    }

    private sealed class FakeProcessor : IProcessadorImagemProduto
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }

        public Task<ImagemProdutoProcessada> ProcessarAsync(
            Stream arquivo,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new ImagemProdutoProcessada(
                new MemoryStream([10, 20, 30, 40, 50, 60, 70], writable: false),
                7,
                640,
                480));
        }
    }

    private sealed class FakeStorage : IArmazenamentoImagemProduto
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public bool FailStore { get; set; }
        public bool FailRead { get; set; }
        public bool FailDelete { get; set; }
        public int DeleteCalls { get; private set; }

        public async Task ArmazenarAsync(
            string chave,
            Stream conteudo,
            CancellationToken cancellationToken = default)
        {
            if (FailStore)
            {
                throw new IOException("Injected storage failure.");
            }

            using var output = new MemoryStream();
            await conteudo.CopyToAsync(output, cancellationToken);
            Objects.Add(chave, output.ToArray());
        }

        public Task<Stream?> AbrirLeituraAsync(
            string chave,
            CancellationToken cancellationToken = default)
        {
            if (FailRead)
            {
                throw new IOException("Injected read failure.");
            }

            return Task.FromResult<Stream?>(Objects.TryGetValue(chave, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : null);
        }

        public Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default) =>
            Task.FromResult(Objects.ContainsKey(chave));

        public Task ExcluirAsync(string chave, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            if (FailDelete)
            {
                throw new IOException("Injected delete failure.");
            }

            Objects.Remove(chave);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : IProdutoImagemOperacaoLogger
    {
        public List<(Guid ProductId, Guid? ImageId, string? Key, string Stage, Exception? Error)> Events
            { get; } = [];

        public void RegistrarFalhaDeArmazenamento(
            Guid produtoId,
            Guid? imagemId,
            string? chave,
            string etapa,
            Exception? exception) =>
            Events.Add((produtoId, imagemId, chave, etapa, exception));
    }
}
