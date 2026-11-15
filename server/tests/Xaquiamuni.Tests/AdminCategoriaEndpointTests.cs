using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
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

public sealed class AdminCategoriaEndpointTests
{
    [Fact]
    public async Task GetCategories_RequiresAuthenticatedAdmin()
    {
        using var factory = CreateFactory(new FakeGerenciarCategorias());
        using var client = CreateClient(factory);

        using var anonymousResponse = await client.GetAsync(AdminCategoriaEndpoints.BasePath);
        using var clientResponse = await SendAsRoleAsync(client, XaquiamuniRoles.Cliente);
        using var employeeResponse = await SendAsRoleAsync(client, XaquiamuniRoles.Funcionario);
        using var adminResponse = await SendAsRoleAsync(client, XaquiamuniRoles.Administrador);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task PostCategory_RequiresValidAntiforgeryAndReturnsCreatedLocation()
    {
        var manager = new FakeGerenciarCategorias();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client, XaquiamuniRoles.Administrador);
        using var invalidRequest = CreatePostRequest(csrf, includeToken: false);
        using var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(0, manager.CadastroCalls);

        using var validRequest = CreatePostRequest(csrf, includeToken: true);
        using var validResponse = await client.SendAsync(validRequest);
        var responseBody = await validResponse.Content.ReadFromJsonAsync<CategoriaResponse>();

        Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
        Assert.Equal($"/api/admin/categorias/{manager.CreatedCategory!.Id}", validResponse.Headers.Location?.OriginalString);
        Assert.Equal(manager.CreatedCategory.Id, responseBody?.Id);
        Assert.Equal(1, manager.CadastroCalls);
    }

    [Fact]
    public async Task GetMissingCategory_ReturnsNotFound()
    {
        using var factory = CreateFactory(new FakeGerenciarCategorias());
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AdminCategoriaEndpoints.BasePath}/{Guid.NewGuid()}");
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeGerenciarCategorias manager) =>
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
                services.RemoveAll<IGerenciarCategorias>();
                services.AddSingleton<IGerenciarCategorias>(manager);
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
        using var request = new HttpRequestMessage(HttpMethod.Get, AdminCategoriaEndpoints.BasePath);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreatePostRequest(CsrfState csrf, bool includeToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AdminCategoriaEndpoints.BasePath)
        {
            Content = JsonContent.Create(new CategoriaWriteRequest
            {
                Nome = "Sabonetes Artesanais",
                Descricao = "Produção própria",
                OrdemExibicao = 0
            })
        };
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        request.Headers.Add("Cookie", $"{AntiforgeryConfiguration.CookieName}={csrf.CookieValue}");
        if (includeToken)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        }

        return request;
    }

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client, string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AntiforgeryConfiguration.TokenEndpoint);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
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
        public const string TestSchemeName = "CategoryTest";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "category-test-user"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                TestSchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeGerenciarCategorias : IGerenciarCategorias
    {
        public int CadastroCalls { get; private set; }
        public Categoria? CreatedCategory { get; private set; }

        public Task<CategoriaResult> CadastrarAsync(
            CategoriaInput? input,
            CancellationToken cancellationToken = default)
        {
            CadastroCalls++;
            if (input is null)
            {
                return Task.FromResult(CategoriaResult.ValidationFailed(
                    new CategoriaValidationError("categoria", "Os dados são obrigatórios.")));
            }

            CreatedCategory = Categoria.Criar(input.Nome, input.Descricao, input.OrdemExibicao);
            return Task.FromResult(CategoriaResult.Success(CreatedCategory));
        }

        public Task<Categoria?> ConsultarAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Categoria?>(null);

        public Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Categoria>>([]);

        public Task<CategoriaResult> EditarAsync(
            Guid id,
            CategoriaInput? input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CategoriaResult.NotFound());

        public Task<CategoriaResult> AlterarStatusAsync(
            Guid id,
            bool? ativa,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CategoriaResult.NotFound());
    }
}
