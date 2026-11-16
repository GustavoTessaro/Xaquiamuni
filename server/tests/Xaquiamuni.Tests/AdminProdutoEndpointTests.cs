using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class AdminProdutoEndpointTests
{
    [Fact]
    public async Task ProductRoutes_RequireAuthenticatedAdminOnly()
    {
        var manager = new FakeGerenciarProdutos();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);

        using var anonymous = await client.GetAsync(AdminProdutoEndpoints.BasePath);
        using var customer = await SendAsRoleAsync(client, XaquiamuniRoles.Cliente);
        using var employee = await SendAsRoleAsync(client, XaquiamuniRoles.Funcionario);
        using var admin = await SendAsRoleAsync(client, XaquiamuniRoles.Administrador);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task EveryProductWriteRoute_RequiresAntiforgeryBeforeCallingTheService()
    {
        var manager = new FakeGerenciarProdutos();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var requests = new[]
        {
            CreateWrite(HttpMethod.Post, AdminProdutoEndpoints.BasePath, new ProdutoCadastroRequest()),
            CreateWrite(HttpMethod.Put, $"{AdminProdutoEndpoints.BasePath}/{productId}", new ProdutoEdicaoRequest()),
            CreateWrite(HttpMethod.Post, $"{AdminProdutoEndpoints.BasePath}/{productId}/variantes", new ProdutoVarianteRequest()),
            CreateWrite(HttpMethod.Put, $"{AdminProdutoEndpoints.BasePath}/{productId}/variantes/{variantId}", new ProdutoVarianteRequest()),
            CreateWrite(HttpMethod.Put, $"{AdminProdutoEndpoints.BasePath}/{productId}/variantes/{variantId}/padrao", null),
            CreateWrite(HttpMethod.Patch, $"{AdminProdutoEndpoints.BasePath}/{productId}/variantes/{variantId}/status", new ProdutoVarianteStatusRequest()),
            CreateWrite(HttpMethod.Patch, $"{AdminProdutoEndpoints.BasePath}/{productId}/publicacao", new ProdutoPublicacaoRequest())
        };

        foreach (var request in requests)
        {
            using (request)
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        Assert.Equal(0, manager.MutationCalls);
    }

    [Fact]
    public async Task CreateProduct_UsesAntiforgeryAndReturnsCreatedLocation()
    {
        var manager = new FakeGerenciarProdutos();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, AdminProdutoEndpoints.BasePath)
        {
            Content = JsonContent.Create(new ProdutoCadastroRequest
            {
                Nome = "Sabonete",
                CategoriaId = Guid.NewGuid(),
                Simples = true,
                VariantePadrao = new ProdutoVarianteRequest
                {
                    UnidadeVenda = "UN",
                    PrecoVenda = 20m,
                    PesoLiquidoGramas = 120.5m
                }
            })
        };
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        request.Headers.Add("Cookie", $"{AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ProdutoResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/admin/produtos/{manager.CreatedProduct!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(manager.CreatedProduct.Id, body?.Id);
        Assert.True(manager.LastCadastroInput?.Simples);
        Assert.Equal(120.5m, manager.LastCadastroInput?.VariantePadrao?.PesoLiquidoGramas);
        Assert.Equal(1, manager.MutationCalls);
    }

    [Fact]
    public async Task GetProduct_ReturnsPersistentCharacteristicAndVariantIds()
    {
        var category = Categoria.Criar("Velas", null, 0);
        var product = Produto.CriarSimples("Vela", null, category, "UN", 25m);
        var color = product.AdicionarCaracteristica("Cor");
        var white = color.AdicionarValor("Branca");
        var variant = Assert.Single(product.Variantes);
        product.AssociarValorCaracteristica(variant.Id, color.Id, white.Id);
        var manager = new FakeGerenciarProdutos { ProductToReturn = product };
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{AdminProdutoEndpoints.BasePath}/{product.Id}");
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ProdutoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(color.Id, Assert.Single(body!.Caracteristicas).Id);
        Assert.Equal(white.Id, Assert.Single(body.Caracteristicas.Single().Valores).Id);
        Assert.Equal(variant.Id, Assert.Single(body.Variantes).Id);
        Assert.Equal(color.Id, Assert.Single(body.Variantes.Single().Caracteristicas).CaracteristicaId);
    }

    [Fact]
    public async Task CreateProduct_MapsTemporaryCharacteristicReferences()
    {
        var manager = new FakeGerenciarProdutos();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, AdminProdutoEndpoints.BasePath)
        {
            Content = JsonContent.Create(new ProdutoCadastroRequest
            {
                Nome = "Vela",
                CategoriaId = Guid.NewGuid(),
                Simples = false,
                VariantePadrao = new ProdutoVarianteRequest
                {
                    Identificacao = "Branca",
                    UnidadeVenda = "UN",
                    PrecoVenda = 30m,
                    Caracteristicas =
                    [
                        new ProdutoVarianteCaracteristicaRequest
                        {
                            ReferenciaCaracteristica = "cor",
                            ReferenciaValor = "branca"
                        }
                    ]
                },
                Caracteristicas =
                [
                    new ProdutoCaracteristicaRequest
                    {
                        Referencia = "cor",
                        Nome = "Cor",
                        Valores =
                        [
                            new ProdutoCaracteristicaValorRequest
                            {
                                Referencia = "branca",
                                Nome = "Branca"
                            }
                        ]
                    }
                ]
            })
        };
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        request.Headers.Add("Cookie", $"{AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var input = Assert.IsType<ProdutoCadastroInput>(manager.LastCadastroInput);
        var characteristic = Assert.IsType<ProdutoCaracteristicaInput>(
            Assert.Single(input.Caracteristicas!));
        Assert.Equal("cor", characteristic.Referencia);
        var variant = Assert.IsType<ProdutoVarianteInput>(input.VariantePadrao);
        var association = Assert.IsType<ProdutoVarianteCaracteristicaInput>(
            Assert.Single(variant.Caracteristicas!));
        Assert.Equal("branca", association.ReferenciaValor);
    }

    [Fact]
    public async Task ListProducts_ForwardsAllFiltersToTheApplicationService()
    {
        var manager = new FakeGerenciarProdutos();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var categoryId = Guid.NewGuid();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{AdminProdutoEndpoints.BasePath}?busca=lavanda&categoriaId={categoryId}&publicado=false");
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ProdutoFiltro("lavanda", categoryId, false), manager.LastFilter);
    }

    [Fact]
    public async Task ListProducts_ReturnsImageCountAndRelativePrimaryImageUrl()
    {
        var category = Categoria.Criar("Velas", null, 0);
        var withImages = Produto.CriarSimples("Vela de Lavanda", null, category, "UN", 25m);
        var primary = withImages.AdicionarImagem(
            "internal-storage-key-1",
            "principal.webp",
            "image/webp",
            1024,
            800,
            600);
        withImages.AdicionarImagem(
            "internal-storage-key-2",
            "detalhe.webp",
            "image/webp",
            2048,
            800,
            600);
        var withoutImages = Produto.CriarSimples("Sabonete", null, category, "UN", 20m);
        var manager = new FakeGerenciarProdutos
        {
            ProductsToReturn = [withImages, withoutImages]
        };
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, AdminProdutoEndpoints.BasePath);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<ProdutoResponse[]>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Collection(
            products!,
            product =>
            {
                Assert.Equal(2, product.QuantidadeImagens);
                Assert.Equal(
                    ProdutoImagemResponse.CriarUrlConteudo(withImages.Id, primary.Id),
                    product.FotoPrincipalUrl);
            },
            product =>
            {
                Assert.Equal(0, product.QuantidadeImagens);
                Assert.Null(product.FotoPrincipalUrl);
            });
        Assert.DoesNotContain("internal-storage-key", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedFailures_ReturnGenericProblemDetailsWithoutExceptionText()
    {
        var manager = new FakeGerenciarProdutos
        {
            ListFailure = new InvalidOperationException("SQL details and connection data")
        };
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, AdminProdutoEndpoints.BasePath);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("SQL details", body, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeGerenciarProdutos manager) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = string.Empty
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestSchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.TestSchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.TestSchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.TestSchemeName,
                        _ => { });
                services.RemoveAll<IGerenciarProdutos>();
                services.AddSingleton<IGerenciarProdutos>(manager);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    private static async Task<HttpResponseMessage> SendAsRoleAsync(HttpClient client, string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AdminProdutoEndpoints.BasePath);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateWrite(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AntiforgeryConfiguration.TokenEndpoint);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = Assert.IsType<string>(
            document.RootElement.GetProperty("requestToken").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var cookieValue = cookie.Split(';', 2)[0].Split('=', 2)[1];

        return new CsrfState(requestToken, cookieValue);
    }

    private sealed record CsrfState(string RequestToken, string CookieValue);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestSchemeName = "ProductTest";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "product-test-user"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                TestSchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName)));
        }
    }

    private sealed class FakeGerenciarProdutos : IGerenciarProdutos
    {
        private readonly Categoria _category = Categoria.Criar("Teste", null, 0);

        public int MutationCalls { get; private set; }
        public Produto? CreatedProduct { get; private set; }
        public Produto? ProductToReturn { get; init; }
        public IReadOnlyList<Produto> ProductsToReturn { get; init; } = [];
        public ProdutoCadastroInput? LastCadastroInput { get; private set; }
        public ProdutoFiltro? LastFilter { get; private set; }
        public Exception? ListFailure { get; init; }

        public Task<IReadOnlyList<Produto>> ListarAsync(
            ProdutoFiltro filtro,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filtro;
            if (ListFailure is not null)
            {
                throw ListFailure;
            }

            return Task.FromResult(ProductsToReturn);
        }

        public Task<Produto?> ConsultarAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductToReturn?.Id == id ? ProductToReturn : null);

        public Task<ProdutoResult> CadastrarAsync(
            ProdutoCadastroInput? input,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            LastCadastroInput = input;
            CreatedProduct = Produto.CriarSimples(
                input?.Nome,
                input?.Descricao,
                _category,
                input?.VariantePadrao?.UnidadeVenda,
                input?.VariantePadrao?.PrecoVenda ?? 0m);
            return Task.FromResult(ProdutoResult.Success(CreatedProduct));
        }

        public Task<ProdutoResult> EditarAsync(
            Guid id,
            ProdutoEdicaoInput? input,
            CancellationToken cancellationToken = default) =>
            MutationCalled();

        public Task<ProdutoResult> AdicionarVarianteAsync(
            Guid produtoId,
            ProdutoVarianteInput? input,
            CancellationToken cancellationToken = default) =>
            MutationCalled();

        public Task<ProdutoResult> EditarVarianteAsync(
            Guid produtoId,
            Guid varianteId,
            ProdutoVarianteInput? input,
            CancellationToken cancellationToken = default) =>
            MutationCalled();

        public Task<ProdutoResult> DefinirVariantePadraoAsync(
            Guid produtoId,
            Guid varianteId,
            CancellationToken cancellationToken = default) =>
            MutationCalled();

        public Task<ProdutoResult> AlterarStatusVarianteAsync(
            Guid produtoId,
            Guid varianteId,
            bool? ativa,
            Guid? novoPadraoId,
            CancellationToken cancellationToken = default) =>
            MutationCalled();

        public Task<ProdutoResult> AlterarPublicacaoAsync(
            Guid produtoId,
            bool? publicado,
            CancellationToken cancellationToken = default) =>
            MutationCalled();

        private Task<ProdutoResult> MutationCalled()
        {
            MutationCalls++;
            return Task.FromResult(ProdutoResult.ProductNotFound());
        }
    }
}
