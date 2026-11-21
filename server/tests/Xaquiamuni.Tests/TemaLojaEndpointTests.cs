using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class TemaLojaEndpointTests
{
    [Fact]
    public async Task LeituraPublicaAnonimaEPublicacaoExclusivaParaAdmin()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SqlServer"] = "" }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication("TemaTeste")
                    .AddScheme<AuthenticationSchemeOptions, PapelTeste>("TemaTeste", _ => { });
                services.RemoveAll<IPublicacaoTemaLoja>();
                services.AddSingleton<IPublicacaoTemaLoja, PublicacaoInicial>();
            });
        });
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        using var publica = await http.GetAsync("/api/loja/tema");
        Assert.Equal(HttpStatusCode.OK, publica.StatusCode);
        var dados = await publica.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("original", dados.GetProperty("chave").GetString());
        Assert.Equal(0, dados.GetProperty("revisao").GetInt32());
        Assert.Equal(2, dados.EnumerateObject().Count());

        foreach (var (papel, status) in new (string?, HttpStatusCode)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (XaquiamuniRoles.Cliente, HttpStatusCode.Forbidden),
            (XaquiamuniRoles.Funcionario, HttpStatusCode.Forbidden),
            (XaquiamuniRoles.Administrador, HttpStatusCode.BadRequest),
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/loja/tema")
            {
                Content = JsonContent.Create(new { chave = "original", revisaoEsperada = 0 }),
            };
            if (papel is not null) request.Headers.Add("Papel-Teste", papel);
            using var resposta = await http.SendAsync(request);
            Assert.Equal(status, resposta.StatusCode);
        }
    }

    private sealed class PublicacaoInicial : IPublicacaoTemaLoja
    {
        public Task<PublicacaoTemaLoja> ConsultarAsync(CancellationToken ct) =>
            Task.FromResult(PublicacaoTemaLoja.Inicial);

        public Task<PublicacaoTemaLoja?> PublicarAsync(string chave, int revisaoEsperada, Guid administradorId,
            Guid idempotencia, CancellationToken ct) => throw new InvalidOperationException("Escrita inesperada.");
    }

    private sealed class PapelTeste(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Papel-Teste", out var papel))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.Role, papel.ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

