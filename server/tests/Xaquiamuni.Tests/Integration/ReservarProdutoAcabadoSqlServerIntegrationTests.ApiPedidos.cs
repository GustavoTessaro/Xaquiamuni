using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    private const string PedidosUrl = PedidosClienteEndpoints.BasePath;

    [SqlServerIntegrationFact]
    public async Task ApiPedidosExigeClienteCsrfERejeitaDadosInvalidos()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        using var factory = CriarApiPedidos(database);
        using var anonimo = ClienteHttpPedidos(factory);
        using var administrador = ClienteHttpPedidos(factory, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        var clienteId = await CriarClienteCompraAsync(context);
        using var cliente = ClienteHttpPedidos(factory, clienteId);
        using var inapto = ClienteHttpPedidos(factory, await CriarClienteCompraAsync(context, confirmarEmail: false));
        var (produto, padrao, _) = await CriarProdutoCompravelAsync(context);
        var itens = new[] { new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = 1 } };
        var corpo = new { itens };
        var chave = Guid.NewGuid();
        foreach (var (client, esperado) in new[]
        {
            (anonimo, HttpStatusCode.Unauthorized),
            (administrador, HttpStatusCode.Forbidden),
        })
        {
            using var lista = await client.GetAsync(PedidosUrl);
            using var detalhe = await client.GetAsync($"{PedidosUrl}/{Guid.NewGuid():D}");
            using var post = await PostPedidoAsync(client, corpo, chave, token: null);
            foreach (var response in new[] { lista, detalhe, post })
            {
                Assert.Equal(esperado, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
        }

        using (var semCsrf = await PostPedidoAsync(cliente, corpo, chave, token: null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        }

        var tokenInapto = await CsrfPedidosAsync(inapto);
        using (var recusado = await PostPedidoAsync(inapto, corpo, chave, tokenInapto))
        {
            Assert.Equal(HttpStatusCode.Forbidden, recusado.StatusCode);
        }

        var token = await CsrfPedidosAsync(cliente);
        object[] invalidos =
        [
            new { itens = Array.Empty<object>() },
            new { itens = new object?[] { null } },
            new { itens, clienteId = Guid.NewGuid() },
            new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = 1, precoUnitarioCentavos = 0 } } },
            new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = "1" } } },
            new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = 0 } } },
            new { itens = Enumerable.Repeat(itens[0], 101).ToArray() },
        ];
        foreach (var invalido in invalidos)
        {
            using var post = await PostPedidoAsync(cliente, invalido, chave, token);
            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        }

        using (var semChave = await PostPedidoAsync(cliente, corpo, chave: null, token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        }

        using (var formatoIncorreto = await PostPedidoAsync(cliente,
            new StringContent("{}", Encoding.UTF8, "text/plain"), chave, token))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, formatoIncorreto.StatusCode);
        }

        using (var chavesDuplicadas = await PostPedidoAsync(cliente,
            new StringContent("{\"itens\":[],\"itens\":[]}", Encoding.UTF8, "application/json"), chave, token))
        {
            using var problema = await LerJsonPedidosAsync(chavesDuplicadas);
            Assert.Equal(HttpStatusCode.BadRequest, chavesDuplicadas.StatusCode);
            Assert.True(problema.RootElement.GetProperty("errors").TryGetProperty("corpo", out _));
        }

        using (var malformado = await PostPedidoAsync(cliente,
            new StringContent("{", Encoding.UTF8, "application/json"), chave, token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformado.StatusCode);
        }

        using (var grande = await PostPedidoAsync(cliente,
            new StringContent(new string(' ', 24 * 1024 + 1), Encoding.UTF8, "application/json"), chave, token))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        }

        using var indisponivel = await PostPedidoAsync(cliente, corpo, chave, token);
        await VerificarConflitoAsync(indisponivel, "itens_indisponiveis");
        using var paginaInvalida = await cliente.GetAsync($"{PedidosUrl}?pagina=2147483647&itensPorPagina=50");
        Assert.Equal(HttpStatusCode.BadRequest, paginaInvalida.StatusCode);
        Assert.Empty(await context.Pedidos.ToListAsync());
        Assert.Empty(await context.ReservasProdutoAcabado.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ApiPedidosCriaRepetePaginaEIsolaPedidosSemBaixaFisica()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var clienteId = await CriarClienteCompraAsync(context);
        var outroId = await CriarClienteCompraAsync(context);
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(context);
        await EntrarAsync(context, padrao.Id, 4, validade: Hoje.AddYears(5));
        using var factory = CriarApiPedidos(database);
        using var cliente = ClienteHttpPedidos(factory, clienteId);
        using var outro = ClienteHttpPedidos(factory, outroId);
        var token = await CsrfPedidosAsync(cliente);
        var outroToken = await CsrfPedidosAsync(outro);
        var item = new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = 1 };
        var chave = Guid.NewGuid();
        using var inicial = await PostPedidoAsync(cliente, new { itens = new[] { item } }, chave, token);
        Assert.Equal(HttpStatusCode.Created, inicial.StatusCode);
        using var json = await LerJsonPedidosAsync(inicial);
        var pedido = json.RootElement;
        var pedidoId = pedido.GetProperty("id").GetGuid();
        var numeroPublico = pedido.GetProperty("numeroPublico").GetString()!;
        Assert.Equal(numeroPublico, NumeroPublicoPedido.Validar(numeroPublico));
        var url = $"{PedidosUrl}/{pedidoId:D}";
        Assert.Equal(url, inicial.Headers.Location?.OriginalString);
        Assert.True(inicial.Headers.CacheControl?.NoStore);
        Assert.Equal("AguardandoPagamento", pedido.GetProperty("situacao").GetString());
        Assert.Equal("RetiradaLocal", pedido.GetProperty("modalidadeEntrega").GetString());
        Assert.Equal("1199", pedido.GetProperty("totalCentavos").GetString());
        var criado = pedido.GetProperty("criadoEmUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, criado.Offset);
        Assert.Equal(criado.AddHours(24), pedido.GetProperty("prazoPagamentoEmUtc").GetDateTimeOffset());
        Assert.Equal("1199", pedido.GetProperty("subtotalProdutosCentavos").GetString());
        Assert.Equal("0", pedido.GetProperty("freteCentavos").GetString());
        Assert.Equal(JsonValueKind.Null, pedido.GetProperty("entrega").ValueKind);
        Assert.Equal(new[]
        {
            "criadoEmUtc", "entrega", "freteCentavos", "id", "itens", "modalidadeEntrega", "numeroPublico", "prazoPagamentoEmUtc",
            "situacao", "subtotalProdutosCentavos", "totalCentavos",
        },
            pedido.EnumerateObject().Select(prop => prop.Name).Order(StringComparer.Ordinal));
        var linha = Assert.Single(pedido.GetProperty("itens").EnumerateArray());
        Assert.Equal(new[] { "nomeProduto", "nomeVariante", "precoUnitarioCentavos", "produtoId", "produtoVarianteId", "quantidade", "subtotalCentavos" },
            linha.EnumerateObject().Select(prop => prop.Name).Order(StringComparer.Ordinal));
        Assert.Equal("1199", linha.GetProperty("precoUnitarioCentavos").GetString());
        Assert.Equal(1, linha.GetProperty("quantidade").GetInt32());

        using var replay = await PostPedidoAsync(cliente, new { itens = new[] { item } }, chave, token);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayJson = await LerJsonPedidosAsync(replay);
        Assert.Equal(pedido.GetRawText(), replayJson.RootElement.GetRawText());
        using var conflito = await PostPedidoAsync(cliente,
            new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = 2 } } }, chave, token);
        await VerificarConflitoAsync(conflito, "chave_em_uso");

        using var parcial = await PostPedidoAsync(cliente, new
        {
            itens = new[] { item, new { produtoId = produto.Id, produtoVarianteId = alternativa.Id, quantidade = 1 } },
        }, Guid.NewGuid(), token);
        await VerificarConflitoAsync(parcial, "itens_indisponiveis");
        Assert.Equal(1, await context.Pedidos.CountAsync());
        Assert.Equal(1, await context.ItensPedido.CountAsync());
        var reserva = await context.ReservasProdutoAcabado.SingleAsync();
        Assert.Equal(pedidoId, reserva.PedidoId);
        Assert.Equal(criado.AddHours(24), reserva.ExpiraEmUtc);

        using var maisRecente = await PostPedidoAsync(cliente, new { itens = new[] { item } }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Created, maisRecente.StatusCode);
        using var maisRecenteJson = await LerJsonPedidosAsync(maisRecente);
        var maisRecenteId = maisRecenteJson.RootElement.GetProperty("id").GetGuid();
        using var pedidoOutro = await PostPedidoAsync(outro, new { itens = new[] { item } }, chave, outroToken);
        Assert.Equal(HttpStatusCode.Created, pedidoOutro.StatusCode);

        using var primeira = await cliente.GetAsync($"{PedidosUrl}?pagina=1&itensPorPagina=1&clienteId={outroId:D}");
        using var segunda = await cliente.GetAsync($"{PedidosUrl}?pagina=2&itensPorPagina=1");
        using var primeiraJson = await LerJsonPedidosAsync(primeira);
        using var segundaJson = await LerJsonPedidosAsync(segunda);
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.True(primeira.Headers.CacheControl?.NoStore);
        Assert.Equal(2, primeiraJson.RootElement.GetProperty("totalItens").GetInt32());
        Assert.Equal(2, primeiraJson.RootElement.GetProperty("totalPaginas").GetInt32());
        var primeiroItem = Assert.Single(primeiraJson.RootElement.GetProperty("itens").EnumerateArray());
        Assert.Equal(maisRecenteId, primeiroItem.GetProperty("id").GetGuid());
        Assert.Equal(new[] { "criadoEmUtc", "id", "numeroPublico", "prazoPagamentoEmUtc", "situacao", "totalCentavos" },
            primeiroItem.EnumerateObject().Select(prop => prop.Name).Order(StringComparer.Ordinal));
        Assert.Equal(pedidoId, Assert.Single(segundaJson.RootElement.GetProperty("itens").EnumerateArray()).GetProperty("id").GetGuid());

        using var detalhe = await cliente.GetAsync(url);
        using var detalheJson = await LerJsonPedidosAsync(detalhe);
        Assert.Equal(HttpStatusCode.OK, detalhe.StatusCode);
        Assert.True(detalhe.Headers.CacheControl?.NoStore);
        Assert.Equal(pedido.GetRawText(), detalheJson.RootElement.GetRawText());
        using var alheio = await outro.GetAsync($"{url}?clienteId={clienteId:D}");
        using var ausente = await outro.GetAsync($"{PedidosUrl}/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        Assert.Equal(ausente.StatusCode, alheio.StatusCode);
        Assert.Equal(await ausente.Content.ReadAsStringAsync(), await alheio.Content.ReadAsStringAsync());
        Assert.Equal(3, await context.Pedidos.CountAsync());
        Assert.Equal(3, await context.ReservasProdutoAcabado.CountAsync());
        Assert.Equal(4, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Checkout_EnderecoPadraoRetornaSomenteProjecaoDoTitularSemAlterarCadastro()
    {
        const string rota = "/api/clientes/me/endereco-padrao";
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var titular = await CriarClienteCompraAsync(context);
        var outro = await CriarClienteCompraAsync(context);
        var semEndereco = await CriarClienteCompraAsync(context);
        var enderecoInvalido = await CriarClienteCompraAsync(context);
        var semPerfil = Guid.NewGuid();
        context.Users.Add(new XaquiamuniUser { Id = semPerfil, UserName = semPerfil.ToString() });
        context.ClienteEnderecos.AddRange(
            ClienteEndereco.Criar(Guid.NewGuid(), titular, "95200-000", "RS", "Vacaria", "Centro", "Rua padrão", "12", "Apto 1", true),
            ClienteEndereco.Criar(Guid.NewGuid(), titular, "95200-000", "RS", "Vacaria", "Centro", "Rua alternativa", "34", null, false),
            ClienteEndereco.Criar(Guid.NewGuid(), outro, "95200-000", "RS", "Vacaria", "Centro", "Rua de outra conta", "56", null, true),
            ClienteEndereco.Criar(Guid.NewGuid(), enderecoInvalido, "95200-000", "RS", "Vacaria", "Centro", "Rua legada", "78", null, true));
        await context.SaveChangesAsync();
        await context.ClienteEnderecos.Where(x => x.ClienteUserId == enderecoInvalido)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Uf, "XX"));

        using var factory = CriarApiPedidos(database);
        using var cliente = ClienteHttpPedidos(factory, titular);
        using var anonimo = ClienteHttpPedidos(factory);
        using var admin = ClienteHttpPedidos(factory, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        using var respostaAnonima = await anonimo.GetAsync(rota);
        using var respostaAdmin = await admin.GetAsync(rota);
        Assert.Equal(HttpStatusCode.Unauthorized, respostaAnonima.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, respostaAdmin.StatusCode);

        using var resposta = await cliente.GetAsync($"{rota}?clienteId={outro:D}&userId={outro:D}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.Private);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("endereco", Assert.Single(corpo.EnumerateObject()).Name);
        var endereco = corpo.GetProperty("endereco");
        Assert.Equal(new[] { "bairro", "cep", "cidade", "complemento", "logradouro", "numero", "uf" },
            endereco.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Rua padrão", endereco.GetProperty("logradouro").GetString());
        Assert.Equal("95200000", endereco.GetProperty("cep").GetString());

        foreach (var id in new[] { semEndereco, semPerfil, enderecoInvalido })
        {
            using var semDados = ClienteHttpPedidos(factory, id);
            using var resultado = await semDados.GetAsync(rota);
            Assert.Equal(HttpStatusCode.OK, resultado.StatusCode);
            var dados = await resultado.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, dados.GetProperty("endereco").ValueKind);
        }

        var persistidos = await context.ClienteEnderecos.AsNoTracking().ToListAsync();
        Assert.Equal(4, persistidos.Count);
        Assert.Equal("XX", Assert.Single(persistidos, x => x.ClienteUserId == enderecoInvalido).Uf);
        Assert.Equal("Rua padrão", Assert.Single(persistidos, x => x.ClienteUserId == titular && x.IsDefault).Logradouro);
    }

    private static WebApplicationFactory<Program> CriarApiPedidos(
        SqlServerTestDatabase database,
        RelogioSqlControlado? relogio = null,
        bool promocoesComerciais = false)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Banco SQL descartavel indisponivel.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false",
                    ["PromocoesComerciais:Habilitadas"] = promocoesComerciais.ToString(),
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<XaquiamuniDbContext>>();
                services.AddDbContext<XaquiamuniDbContext>(options =>
                {
                    options.UseSqlServer(connectionString);
                    if (relogio is not null)
                    {
                        options.AddInterceptors(relogio);
                    }
                });
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = AutenticacaoTestePedidos.Scheme;
                    options.DefaultChallengeScheme = AutenticacaoTestePedidos.Scheme;
                    options.DefaultForbidScheme = AutenticacaoTestePedidos.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, AutenticacaoTestePedidos>(
                    AutenticacaoTestePedidos.Scheme, _ => { });
            });
        });
    }

    private static HttpClient ClienteHttpPedidos(
        WebApplicationFactory<Program> factory, Guid? clienteId = null, string papel = XaquiamuniRoles.Cliente)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        if (clienteId is Guid id)
        {
            client.DefaultRequestHeaders.Add("X-Test-User-Id", id.ToString("D"));
            client.DefaultRequestHeaders.Add("X-Test-Role", papel);
        }
        return client;
    }

    private static async Task<string> CsrfPedidosAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var json = await LerJsonPedidosAsync(response);
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostPedidoAsync(
        HttpClient client, object body, Guid? chave, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PedidosUrl)
        {
            Content = body is HttpContent content ? content : JsonContent.Create(body, body.GetType()),
        };
        if (chave is Guid id)
        {
            request.Headers.Add("Idempotency-Key", id.ToString("D"));
        }
        if (token is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, token);
        }
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> LerJsonPedidosAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task VerificarConflitoAsync(HttpResponseMessage response, string codigo)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await LerJsonPedidosAsync(response);
        Assert.Equal(codigo, json.RootElement.GetProperty("codigo").GetString());
    }

    private sealed class AutenticacaoTestePedidos(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "PedidosHttpIntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User-Id", out var id)
                || !Request.Headers.TryGetValue("X-Test-Role", out var papel))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, id.ToString()), new(ClaimTypes.Role, papel.ToString())], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
