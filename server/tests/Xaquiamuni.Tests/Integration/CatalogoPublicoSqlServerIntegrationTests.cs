using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Catalogo.Publico;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class CatalogoPublicoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task PublicCatalog_ListsActiveCategoriesAndOnlyVisibleProductsWithSearchAndPagination()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var storage = new TemporaryImageStorage();
        using var factory = CreateFactory(database, storage.RootPath);
        using var client = CreateClient(factory);
        var catalog = await SeedCatalogAsync(database, factory);

        using var categoriesResponse = await client.GetAsync($"{CatalogoPublicoEndpoints.BasePath}/categorias");
        var categories = await categoriesResponse.Content
            .ReadFromJsonAsync<CategoriaCatalogoPublicoResponse[]>();
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        Assert.Equal(
            new[] { catalog.SecondaryActiveCategoryId, catalog.ActiveCategoryId },
            categories!.Select(category => category.Id));

        using var firstPageResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos?pagina=1&itensPorPagina=1");
        var firstPage = await firstPageResponse.Content
            .ReadFromJsonAsync<CatalogoPublicoPaginaResponse>();
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        Assert.Equal(2, firstPage!.TotalItens);
        Assert.Equal(2, firstPage.TotalPaginas);
        Assert.Equal("Sabonete simples", Assert.Single(firstPage.Itens).Nome);

        using var secondPageResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos?pagina=2&itensPorPagina=1");
        var secondPage = await secondPageResponse.Content
            .ReadFromJsonAsync<CatalogoPublicoPaginaResponse>();
        Assert.Equal("Vela artesanal", Assert.Single(secondPage!.Itens).Nome);

        using var filteredResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos"
            + $"?busca=vela&categoriaId={catalog.ActiveCategoryId:D}");
        var filtered = await filteredResponse.Content
            .ReadFromJsonAsync<CatalogoPublicoPaginaResponse>();
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        Assert.Equal(1, filtered!.TotalItens);
        Assert.Equal(catalog.MultiOptionProductId, Assert.Single(filtered.Itens).Id);
        Assert.Equal(20m, filtered.Itens[0].PrecoMinimo);
        Assert.Equal(30m, filtered.Itens[0].PrecoMaximo);
        Assert.Equal(new[] { "KIT", "UN" }, filtered.Itens[0].UnidadesVenda);
        Assert.Equal(2, filtered.Itens[0].Medidas.Count);
        Assert.Equal(catalog.MainImageId, filtered.Itens[0].FotoPrincipal.Id);
        Assert.Equal(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos/{catalog.MultiOptionProductId:D}"
            + $"/imagens/{catalog.MainImageId:D}/conteudo",
            filtered.Itens[0].FotoPrincipal.ConteudoUrl);

        using var invalidPageResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos?pagina=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);

        using var privateCategoryResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos"
            + $"?categoriaId={catalog.InactiveCategoryId:D}");
        var privateCategory = await privateCategoryResponse.Content
            .ReadFromJsonAsync<CatalogoPublicoPaginaResponse>();
        Assert.Equal(0, privateCategory!.TotalItens);
    }

    [SqlServerIntegrationFact]
    public async Task PublicProductDetails_ExposeOnlyActiveOptionsRealCombinationsAndOrderedGallery()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var storage = new TemporaryImageStorage();
        using var factory = CreateFactory(database, storage.RootPath);
        using var client = CreateClient(factory);
        var catalog = await SeedCatalogAsync(database, factory);

        using var detailsResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos/{catalog.MultiOptionProductId:D}");
        var details = await detailsResponse.Content
            .ReadFromJsonAsync<ProdutoCatalogoDetalheResponse>();

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Equal("Vela artesanal", details!.Nome);
        Assert.Equal("Descrição pública", details.Descricao);
        Assert.True(details.RequerSelecaoDeOpcao);
        Assert.Equal(catalog.MainVariantId, details.OpcaoPrincipalId);
        Assert.Equal(
            new[] { catalog.MainImageId, catalog.SecondaryImageId },
            details.Galeria.Select(imagem => imagem.Id));
        Assert.Equal(catalog.MainImageId, details.FotoPrincipal.Id);
        Assert.Equal(new[] { catalog.MainVariantId }, details.Galeria[0].OpcaoIds);
        Assert.Equal(new[] { catalog.SecondaryVariantId }, details.Galeria[1].OpcaoIds);

        Assert.Equal(
            new[] { catalog.MainVariantId, catalog.SecondaryVariantId },
            details.Opcoes.Select(opcao => opcao.Id));
        Assert.All(details.Opcoes, opcao => Assert.NotEqual(catalog.InactiveVariantId, opcao.Id));
        Assert.Equal(
            new[] { "Baunilha", "Lavanda" },
            details.Opcoes
                .Select(opcao => Assert.Single(opcao.Caracteristicas).ValorNome)
                .OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(100m, details.Opcoes.Single(opcao => opcao.Id == catalog.MainVariantId).PesoLiquidoGramas);
        Assert.Equal(
            200m,
            details.Opcoes.Single(opcao => opcao.Id == catalog.SecondaryVariantId).VolumeLiquidoMililitros);
        Assert.Equal(
            new[] { "Baunilha", "Lavanda" },
            Assert.Single(details.Caracteristicas).Valores.Select(valor => valor.Nome));
        Assert.DoesNotContain(
            details.Caracteristicas.Single().Valores,
            valor => valor.Nome is "Eucalipto" or "Canela");

        using var simpleDetailsResponse = await client.GetAsync(
            $"{CatalogoPublicoEndpoints.BasePath}/produtos/{catalog.SimpleProductId:D}");
        var simpleDetails = await simpleDetailsResponse.Content
            .ReadFromJsonAsync<ProdutoCatalogoDetalheResponse>();
        Assert.Equal(HttpStatusCode.OK, simpleDetailsResponse.StatusCode);
        Assert.False(simpleDetails!.RequerSelecaoDeOpcao);
        Assert.Null(Assert.Single(simpleDetails.Opcoes).Identificacao);
        Assert.Empty(Assert.Single(simpleDetails.Opcoes).Caracteristicas);

        foreach (var unavailableProductId in new[]
                 {
                     catalog.UnpublishedProductId,
                     catalog.InactiveCategoryProductId,
                     catalog.LegacyProductWithoutImageId
                 })
        {
            using var unavailableResponse = await client.GetAsync(
                $"{CatalogoPublicoEndpoints.BasePath}/produtos/{unavailableProductId:D}");
            Assert.Equal(HttpStatusCode.NotFound, unavailableResponse.StatusCode);
        }

        var serialized = await detailsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ChaveArmazenamento", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NomeOriginal", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerIntegrationFact]
    public async Task PublicCatalog_UsesCommercialAvailabilityWithoutExposingStockOrOrders()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var storage = new TemporaryImageStorage();
        using var factory = CreateFactory(database, storage.RootPath);
        using var client = CreateClient(factory);
        var catalog = await SeedCatalogAsync(database, factory);
        await using var context = database.CreateVerificationContext();
        var agora = await context.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync();
        var calendario = database.Services.GetRequiredService<ICalendarioOperacionalEstoque>();
        var hoje = calendario.ObterDataEm(agora);
        var estoque = database.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var principal = await EntradaAsync(catalog.MainVariantId, hoje, CondicaoEstoqueProdutoAcabado.Vendavel);
        var secundaria = await EntradaAsync(catalog.SecondaryVariantId, hoje, CondicaoEstoqueProdutoAcabado.Vendavel);
        await EntradaAsync(catalog.MainVariantId, hoje.AddDays(-1), CondicaoEstoqueProdutoAcabado.Vendavel);
        await EntradaAsync(catalog.MainVariantId, hoje, CondicaoEstoqueProdutoAcabado.Quarentena);
        await EntradaAsync(catalog.InactiveVariantId, hoje, CondicaoEstoqueProdutoAcabado.Vendavel);

        var ativa = ReservaProdutoAcabado.Criar(Guid.NewGuid(), catalog.MainVariantId, principal, 1, agora.AddHours(-1));
        var confirmada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), catalog.MainVariantId, principal, 1, agora.AddDays(-2));
        confirmada.ConfirmarCompromisso(confirmada.CriadaEmUtc.AddHours(1));
        var expirada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), catalog.SecondaryVariantId, secundaria, 1, agora.AddDays(-2));
        expirada.Expirar(agora);
        var ativaVencida = ReservaProdutoAcabado.Criar(Guid.NewGuid(), catalog.SecondaryVariantId, secundaria, 1, agora.AddDays(-2));
        var liberada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), catalog.SecondaryVariantId, secundaria, 1, agora.AddHours(-1));
        liberada.Liberar(agora, "Cancelamento autorizado");
        context.ReservasProdutoAcabado.AddRange(ativa, confirmada, expirada, ativaVencida, liberada);
        await context.SaveChangesAsync();

        var pagina = await client.GetFromJsonAsync<CatalogoPublicoPaginaResponse>("/api/catalogo/produtos");
        Assert.NotNull(pagina);
        Assert.True(Assert.Single(pagina.Itens, p => p.Id == catalog.MultiOptionProductId).Disponivel);
        Assert.False(Assert.Single(pagina.Itens, p => p.Id == catalog.SimpleProductId).Disponivel);

        using var resposta = await client.GetAsync($"/api/catalogo/produtos/{catalog.MultiOptionProductId:D}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var detalhe = await resposta.Content.ReadFromJsonAsync<ProdutoCatalogoDetalheResponse>();
        Assert.NotNull(detalhe);
        Assert.True(detalhe.Disponivel);
        Assert.Equal(catalog.MainVariantId, detalhe.OpcaoPrincipalId);
        Assert.False(Assert.Single(detalhe.Opcoes, o => o.Id == catalog.MainVariantId).Disponivel);
        Assert.True(Assert.Single(detalhe.Opcoes, o => o.Id == catalog.SecondaryVariantId).Disponivel);
        Assert.DoesNotContain(detalhe.Opcoes, o => o.Id == catalog.InactiveVariantId);
        var simples = await client.GetFromJsonAsync<ProdutoCatalogoDetalheResponse>(
            $"/api/catalogo/produtos/{catalog.SimpleProductId:D}");
        Assert.NotNull(simples);
        Assert.False(simples.Disponivel);
        Assert.False(Assert.Single(simples.Opcoes).Disponivel);

        var consultaAdmin = new EfEstoqueProdutoAcabadoConsultaRepository(context);
        foreach (var opcao in detalhe.Opcoes)
        {
            var posicao = await consultaAdmin.ConsultarPosicaoVarianteAsync(opcao.Id, hoje);
            Assert.Equal(posicao!.Resumo.SaldoDisponivel > 0, opcao.Disponivel);
        }

        var json = await resposta.Content.ReadAsStringAsync();
        foreach (var privado in new[] { "loteId", "pedidoId", "saldo", "quantidade", "reserva", "cliente", principal.ToString(), ativa.PedidoId.ToString() })
        {
            Assert.DoesNotContain(privado, json, StringComparison.OrdinalIgnoreCase);
        }

        async Task<Guid> EntradaAsync(Guid variante, DateOnly validade, CondicaoEstoqueProdutoAcabado condicao)
        {
            var resultado = await estoque.RegistrarEntradaAsync(new(
                variante, Guid.NewGuid(), TipoMovimentoProdutoAcabado.EntradaManual, 2, condicao,
                "Estoque de teste", Guid.NewGuid().ToString("N"), DataFabricacaoNovoLote: hoje.AddDays(-2),
                DataValidadeNovoLote: validade));
            Assert.True(resultado.Sucesso, resultado.Erro);
            return resultado.Lote!.Id;
        }
    }

    [SqlServerIntegrationFact]
    public async Task PublicImageContent_IsAnonymousForVisibleProductsAndHiddenForUnpublishedOrInactiveProducts()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var storage = new TemporaryImageStorage();
        using var factory = CreateFactory(database, storage.RootPath);
        using var client = CreateClient(factory);
        var catalog = await SeedCatalogAsync(database, factory);

        var publicImageUrl = $"{CatalogoPublicoEndpoints.BasePath}/produtos/{catalog.MultiOptionProductId:D}"
            + $"/imagens/{catalog.MainImageId:D}/conteudo";
        using var publicImageResponse = await client.GetAsync(publicImageUrl);
        var publicImageContent = await publicImageResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, publicImageResponse.StatusCode);
        Assert.Equal("image/webp", publicImageResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(publicImageResponse.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", Assert.Single(publicImageResponse.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(catalog.MainImageContent, publicImageContent);

        foreach (var (productId, imageId) in new[]
                 {
                     (catalog.UnpublishedProductId, catalog.UnpublishedImageId),
                     (catalog.InactiveCategoryProductId, catalog.InactiveCategoryImageId)
                 })
        {
            using var privateImageResponse = await client.GetAsync(
                $"{CatalogoPublicoEndpoints.BasePath}/produtos/{productId:D}"
                + $"/imagens/{imageId:D}/conteudo");
            Assert.Equal(HttpStatusCode.NotFound, privateImageResponse.StatusCode);
        }

        using var adminResponse = await client.GetAsync(AdminProdutoEndpoints.BasePath);
        Assert.Equal(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
    }

    private static async Task<SeededCatalog> SeedCatalogAsync(
        SqlServerTestDatabase database,
        WebApplicationFactory<Program> factory)
    {
        var mainImageContent = Encoding.UTF8.GetBytes("public-webp-content");
        var secondaryImageContent = Encoding.UTF8.GetBytes("secondary-webp-content");
        var simpleImageContent = Encoding.UTF8.GetBytes("simple-webp-content");
        var unpublishedImageContent = Encoding.UTF8.GetBytes("unpublished-webp-content");
        var inactiveImageContent = Encoding.UTF8.GetBytes("inactive-webp-content");
        var legacyImageContent = Encoding.UTF8.GetBytes("legacy-webp-content");

        Guid activeCategoryId;
        Guid secondaryActiveCategoryId;
        Guid inactiveCategoryId;
        Guid multiOptionProductId;
        Guid simpleProductId;
        Guid unpublishedProductId;
        Guid inactiveCategoryProductId;
        Guid legacyProductWithoutImageId;
        Guid mainVariantId;
        Guid secondaryVariantId;
        Guid inactiveVariantId;
        Guid mainImageId;
        Guid secondaryImageId;
        Guid unpublishedImageId;
        Guid inactiveCategoryImageId;
        Guid legacyImageId;
        string mainImageKey;
        string secondaryImageKey;
        string simpleImageKey;
        string unpublishedImageKey;
        string inactiveImageKey;
        string legacyImageKey;

        await using (var context = database.CreateVerificationContext())
        {
            var activeCategory = Categoria.Criar("Aromas", null, 1);
            var secondaryActiveCategory = Categoria.Criar("Banho", null, 0);
            var inactiveCategory = Categoria.Criar("Rascunhos", null, 2);
            context.Categorias.AddRange(activeCategory, secondaryActiveCategory, inactiveCategory);
            await context.SaveChangesAsync();

            var mainVariant = ProdutoVariante.Criar(
                "Lavanda 100 g",
                "UN",
                20m,
                padrao: true,
                pesoLiquidoGramas: 100m);
            var secondaryVariant = ProdutoVariante.Criar(
                "Baunilha 200 ml",
                "KIT",
                30m,
                volumeLiquidoMililitros: 200m);
            var inactiveVariant = ProdutoVariante.Criar("Canela", "UN", 999m, ativa: false);
            var multiOptionProduct = Produto.Criar(
                "Vela artesanal",
                "Descrição pública",
                activeCategory,
                [mainVariant, secondaryVariant, inactiveVariant]);
            var fragrance = multiOptionProduct.AdicionarCaracteristica("Fragrância");
            var lavender = fragrance.AdicionarValor("Lavanda");
            var vanilla = fragrance.AdicionarValor("Baunilha");
            var cinnamon = fragrance.AdicionarValor("Canela");
            _ = fragrance.AdicionarValor("Eucalipto");
            multiOptionProduct.AssociarValorCaracteristica(mainVariant.Id, fragrance.Id, lavender.Id);
            multiOptionProduct.AssociarValorCaracteristica(secondaryVariant.Id, fragrance.Id, vanilla.Id);
            multiOptionProduct.AssociarValorCaracteristica(inactiveVariant.Id, fragrance.Id, cinnamon.Id);
            var mainImage = AddImage(multiOptionProduct, "principal", mainImageContent);
            var secondaryImage = AddImage(multiOptionProduct, "secundaria", secondaryImageContent);
            multiOptionProduct.AssociarImagemAVariante(mainImage.Image.Id, mainVariant.Id);
            multiOptionProduct.AssociarImagemAVariante(mainImage.Image.Id, inactiveVariant.Id);
            multiOptionProduct.AssociarImagemAVariante(secondaryImage.Image.Id, secondaryVariant.Id);
            multiOptionProduct.Publicar();

            var simpleProduct = Produto.CriarSimples("Sabonete simples", null, secondaryActiveCategory, "UN", 10m);
            var simpleImage = AddImage(simpleProduct, "simples", simpleImageContent);
            simpleProduct.Publicar();

            var unpublishedProduct = Produto.CriarSimples("Produto em rascunho", null, activeCategory, "UN", 5m);
            var unpublishedImage = AddImage(unpublishedProduct, "rascunho", unpublishedImageContent);

            var inactiveCategoryProduct = Produto.CriarSimples(
                "Produto de categoria inativa",
                null,
                inactiveCategory,
                "UN",
                8m);
            var inactiveCategoryImage = AddImage(
                inactiveCategoryProduct,
                "categoria-inativa",
                inactiveImageContent);
            inactiveCategoryProduct.Publicar();

            var legacyProductWithoutImage = Produto.CriarSimples(
                "Publicado legado sem foto",
                null,
                activeCategory,
                "UN",
                7m);
            var legacyImage = AddImage(legacyProductWithoutImage, "legado", legacyImageContent);
            legacyProductWithoutImage.Publicar();
            inactiveCategory.DefinirAtiva(false);

            context.Produtos.AddRange(
                multiOptionProduct,
                simpleProduct,
                unpublishedProduct,
                inactiveCategoryProduct,
                legacyProductWithoutImage);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [ProdutoImagens] WHERE [Id] = {legacyImage.Image.Id}");

            activeCategoryId = activeCategory.Id;
            secondaryActiveCategoryId = secondaryActiveCategory.Id;
            inactiveCategoryId = inactiveCategory.Id;
            multiOptionProductId = multiOptionProduct.Id;
            simpleProductId = simpleProduct.Id;
            unpublishedProductId = unpublishedProduct.Id;
            inactiveCategoryProductId = inactiveCategoryProduct.Id;
            legacyProductWithoutImageId = legacyProductWithoutImage.Id;
            mainVariantId = mainVariant.Id;
            secondaryVariantId = secondaryVariant.Id;
            inactiveVariantId = inactiveVariant.Id;
            mainImageId = mainImage.Image.Id;
            secondaryImageId = secondaryImage.Image.Id;
            unpublishedImageId = unpublishedImage.Image.Id;
            inactiveCategoryImageId = inactiveCategoryImage.Image.Id;
            legacyImageId = legacyImage.Image.Id;
            mainImageKey = mainImage.Key;
            secondaryImageKey = secondaryImage.Key;
            simpleImageKey = simpleImage.Key;
            unpublishedImageKey = unpublishedImage.Key;
            inactiveImageKey = inactiveCategoryImage.Key;
            legacyImageKey = legacyImage.Key;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var imageStorage = scope.ServiceProvider.GetRequiredService<IArmazenamentoImagemProduto>();
            await StoreAsync(imageStorage, mainImageKey, mainImageContent);
            await StoreAsync(imageStorage, secondaryImageKey, secondaryImageContent);
            await StoreAsync(imageStorage, simpleImageKey, simpleImageContent);
            await StoreAsync(imageStorage, unpublishedImageKey, unpublishedImageContent);
            await StoreAsync(imageStorage, inactiveImageKey, inactiveImageContent);
            await StoreAsync(imageStorage, legacyImageKey, legacyImageContent);
        }

        return new SeededCatalog(
            activeCategoryId,
            secondaryActiveCategoryId,
            inactiveCategoryId,
            multiOptionProductId,
            simpleProductId,
            unpublishedProductId,
            inactiveCategoryProductId,
            legacyProductWithoutImageId,
            mainVariantId,
            secondaryVariantId,
            inactiveVariantId,
            mainImageId,
            secondaryImageId,
            unpublishedImageId,
            inactiveCategoryImageId,
            legacyImageId,
            mainImageContent);
    }

    private static StoredImage AddImage(Produto produto, string name, byte[] content)
    {
        var key = ChaveArmazenamentoImagemProduto.Gerar();
        var image = produto.AdicionarImagem(
            key,
            $"{name}.webp",
            "image/webp",
            content.LongLength,
            64,
            64);
        return new StoredImage(key, image);
    }

    private static async Task StoreAsync(
        IArmazenamentoImagemProduto storage,
        string key,
        byte[] content)
    {
        await using var stream = new MemoryStream(content);
        await storage.ArmazenarAsync(key, stream);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        SqlServerTestDatabase database,
        string storagePath)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["ProductImages:Storage:RootPath"] = storagePath,
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(options => options.UseSqlServer(connectionString));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestSchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.TestSchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.TestSchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.TestSchemeName,
                        _ => { });
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    private sealed record StoredImage(string Key, ProdutoImagem Image);

    private sealed record SeededCatalog(
        Guid ActiveCategoryId,
        Guid SecondaryActiveCategoryId,
        Guid InactiveCategoryId,
        Guid MultiOptionProductId,
        Guid SimpleProductId,
        Guid UnpublishedProductId,
        Guid InactiveCategoryProductId,
        Guid LegacyProductWithoutImageId,
        Guid MainVariantId,
        Guid SecondaryVariantId,
        Guid InactiveVariantId,
        Guid MainImageId,
        Guid SecondaryImageId,
        Guid UnpublishedImageId,
        Guid InactiveCategoryImageId,
        Guid LegacyImageId,
        byte[] MainImageContent);

    private sealed class TemporaryImageStorage : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"xaquiamuni-public-catalog-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestSchemeName = "PublicCatalogIntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
