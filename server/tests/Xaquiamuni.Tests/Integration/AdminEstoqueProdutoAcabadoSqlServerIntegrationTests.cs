using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AdminEstoqueProdutoAcabadoSqlServerIntegrationTests
{
    private static readonly Guid AdminId =
        Guid.Parse("cd4d0df3-43be-4e42-9ba9-a8da1d03ef29");

    [SqlServerIntegrationFact]
    public async Task Endpoints_RequireAdminAndAntiforgery()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);

        using var anonymous = CreateClient(factory);
        using var anonResponse = await anonymous.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/posicao");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        using var client = CreateClient(factory, XaquiamuniRoles.Cliente);
        using var clientResponse = await client.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/posicao");
        Assert.Equal(HttpStatusCode.Forbidden, clientResponse.StatusCode);

        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var emptyGuid = Guid.Empty.ToString("D");
        using var emptyVariantResponse = await admin.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{emptyGuid}/posicao");
        await AssertValidationProblemAsync(emptyVariantResponse);

        using var emptyLotResponse = await admin.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/lotes/{emptyGuid}");
        await AssertValidationProblemAsync(emptyLotResponse);

        using var emptyMovementResponse = await admin.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/movimentacoes/{emptyGuid}");
        await AssertValidationProblemAsync(emptyMovementResponse);

        using var missingVariantResponse = await admin.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/posicao");
        Assert.Equal(HttpStatusCode.NotFound, missingVariantResponse.StatusCode);

        using var missingLotResponse = await admin.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/lotes/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missingLotResponse.StatusCode);

        using var missingMovementResponse = await admin.GetAsync(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/movimentacoes/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missingMovementResponse.StatusCode);

        using var emptyVariantEntryRequest = CreateWriteRequest(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{emptyGuid}/entradas",
            new EntradaProdutoAcabadoAdminRequest
            {
                Tipo = TipoMovimentoProdutoAcabado.EntradaManual,
                Quantidade = 1,
                CondicaoDestino = CondicaoEstoqueProdutoAcabado.Vendavel,
                Motivo = "Variante vazia"
            },
            "http-empty-variant-entry",
            csrf.RequestToken);
        using var emptyVariantEntryResponse = await admin.SendAsync(emptyVariantEntryRequest);
        await AssertValidationProblemAsync(emptyVariantEntryResponse);

        using var emptyMovementReversalRequest = CreateWriteRequest(
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/movimentacoes/{emptyGuid}/estorno",
            new EstornoProdutoAcabadoAdminRequest { Motivo = "Movimentação vazia" },
            "http-empty-movement-reversal",
            csrf.RequestToken);
        using var emptyMovementReversalResponse = await admin.SendAsync(emptyMovementReversalRequest);
        await AssertValidationProblemAsync(emptyMovementReversalResponse);

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{Guid.NewGuid():D}/politica")
        {
            Content = JsonContent.Create(new PoliticaProdutoAcabadoAdminRequest
            {
                EstoqueMinimo = 2,
                PrazoValidadePadraoDias = 30
            })
        };
        using var noAntiforgery = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, noAntiforgery.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task Endpoints_ConfigureEntryCountReversalAndHistoryWithAuthenticatedActor()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var seedContext = database.CreateVerificationContext();
        var category = Categoria.Criar($"Categoria {Guid.NewGuid():N}", null, 0);
        seedContext.Categorias.Add(category);
        await seedContext.SaveChangesAsync();
        var variant = ProdutoVariante.Criar("Padrão", "UN", 12m, padrao: true);
        var product = Produto.Criar($"Produto {Guid.NewGuid():N}", null, category, [variant]);
        seedContext.Produtos.Add(product);
        await seedContext.SaveChangesAsync();

        using var factory = CreateFactory(database);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var basePath = AdminEstoqueProdutoAcabadoEndpoints.BasePath;

        using var policyRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"{basePath}/variantes/{variant.Id:D}/politica")
        {
            Content = JsonContent.Create(new PoliticaProdutoAcabadoAdminRequest
            {
                EstoqueMinimo = 2,
                PrazoValidadePadraoDias = 30
            })
        };
        policyRequest.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        using var policyResponse = await admin.SendAsync(policyRequest);
        Assert.Equal(HttpStatusCode.OK, policyResponse.StatusCode);

        var entryRequestBody = new EntradaProdutoAcabadoAdminRequest
        {
            Tipo = TipoMovimentoProdutoAcabado.EstoqueInicial,
            Quantidade = 5,
            CondicaoDestino = CondicaoEstoqueProdutoAcabado.Vendavel,
            DataFabricacao = new DateOnly(2026, 11, 17),
            DataValidade = new DateOnly(2026, 12, 17),
            Motivo = "Abertura HTTP"
        };
        using var entryRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/entradas",
            entryRequestBody,
            "http-finished-entry",
            csrf.RequestToken);
        using var entryResponse = await admin.SendAsync(entryRequest);
        Assert.Equal(HttpStatusCode.Created, entryResponse.StatusCode);
        var entry = await entryResponse.Content
            .ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse>();
        Assert.NotNull(entry);
        Assert.Equal(AdminId, entry!.ResponsavelId);
        Assert.False(string.IsNullOrWhiteSpace(entry.CodigoLote));

        using var entryReplayRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/entradas",
            entryRequestBody,
            "http-finished-entry",
            csrf.RequestToken);
        using var entryReplayResponse = await admin.SendAsync(entryReplayRequest);
        Assert.Equal(HttpStatusCode.OK, entryReplayResponse.StatusCode);
        var entryReplay = await entryReplayResponse.Content
            .ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse>();
        Assert.NotNull(entryReplay);
        Assert.Equal(entry!.Id, entryReplay!.Id);
        Assert.Equal(entry.CodigoLote, entryReplay.CodigoLote);

        var reserva = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variant.Id, entry.LoteId, 1, DateTimeOffset.UtcNow);
        seedContext.ReservasProdutoAcabado.Add(reserva);
        await seedContext.SaveChangesAsync();

        using var positionResponse = await admin.GetAsync(
            $"{basePath}/variantes/{variant.Id:D}/posicao");
        Assert.Equal(HttpStatusCode.OK, positionResponse.StatusCode);
        var position = await positionResponse.Content
            .ReadFromJsonAsync<PosicaoProdutoAcabadoAdminResponse>();
        Assert.NotNull(position);
        Assert.Equal(5, position!.SaldoFisico);
        Assert.Equal(5, position.SaldoUtilizavel);
        Assert.Equal(1, position.SaldoReservado);
        Assert.Equal(4, position.SaldoDisponivel);
        Assert.Equal(2, position.EstoqueMinimo);

        var lote = Assert.Single(position.Lotes);
        Assert.Equal(lote.Codigo, entry.CodigoLote);
        Assert.Equal(1, lote.SaldoReservado);
        Assert.Equal(4, lote.SaldoDisponivel);
        using var responseLote = await admin.GetAsync($"{basePath}/variantes/{variant.Id:D}/lotes/{lote.LoteId:D}");
        Assert.Equal(HttpStatusCode.OK, responseLote.StatusCode);
        var jsonLote = await responseLote.Content.ReadAsStringAsync();
        Assert.DoesNotContain("pedidoId", jsonLote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(reserva.Id.ToString(), jsonLote, StringComparison.OrdinalIgnoreCase);

        using var exitRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/saidas",
            new SaidaProdutoAcabadoAdminRequest
            {
                Tipo = TipoMovimentoProdutoAcabado.SaidaManual,
                LoteId = lote.LoteId,
                Quantidade = 1,
                CondicaoOrigem = CondicaoEstoqueProdutoAcabado.Vendavel,
                Motivo = "Saída HTTP"
            },
            "http-finished-exit",
            csrf.RequestToken);
        using var exitResponse = await admin.SendAsync(exitRequest);
        Assert.Equal(HttpStatusCode.Created, exitResponse.StatusCode);
        var exit = await exitResponse.Content.ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse>();
        Assert.NotNull(exit);
        Assert.Equal(lote.Codigo, exit!.CodigoLote);

        var transferRequestBody = new TransferenciaProdutoAcabadoAdminRequest
        {
            LoteId = lote.LoteId,
            Quantidade = 1,
            CondicaoOrigem = CondicaoEstoqueProdutoAcabado.Vendavel,
            CondicaoDestino = CondicaoEstoqueProdutoAcabado.Quarentena,
            Motivo = "Transferência HTTP"
        };
        using var transferRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/transferencias",
            transferRequestBody,
            "http-finished-transfer",
            csrf.RequestToken);
        using var transferResponse = await admin.SendAsync(transferRequest);
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transfer = await transferResponse.Content
            .ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse>();
        Assert.NotNull(transfer);
        Assert.Equal(lote.Codigo, transfer!.CodigoLote);

        using var transferReplayRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/transferencias",
            transferRequestBody,
            "http-finished-transfer",
            csrf.RequestToken);
        using var transferReplayResponse = await admin.SendAsync(transferReplayRequest);
        Assert.Equal(HttpStatusCode.OK, transferReplayResponse.StatusCode);
        var transferReplay = await transferReplayResponse.Content
            .ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse>();
        Assert.NotNull(transferReplay);
        Assert.Equal(transfer!.Id, transferReplay!.Id);
        Assert.Equal(lote.Codigo, transferReplay.CodigoLote);

        using var transferReversalRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/movimentacoes/{transfer.Id:D}/estorno",
            new EstornoProdutoAcabadoAdminRequest { Motivo = "Estorno da transferência HTTP" },
            "http-finished-transfer-reversal",
            csrf.RequestToken);
        using var transferReversalResponse = await admin.SendAsync(transferReversalRequest);
        Assert.Equal(HttpStatusCode.Created, transferReversalResponse.StatusCode);
        var transferReversal = await transferReversalResponse.Content
            .ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse>();
        Assert.NotNull(transferReversal);
        Assert.Equal(lote.Codigo, transferReversal!.CodigoLote);
        Assert.Equal(transfer.Id, transferReversal.MovimentoOriginalId);

        using var beforeCountPositionResponse = await admin.GetAsync(
            $"{basePath}/variantes/{variant.Id:D}/posicao");
        Assert.Equal(HttpStatusCode.OK, beforeCountPositionResponse.StatusCode);
        var beforeCountPosition = await beforeCountPositionResponse.Content
            .ReadFromJsonAsync<PosicaoProdutoAcabadoAdminResponse>();
        Assert.NotNull(beforeCountPosition);
        var loteAntesContagem = Assert.Single(beforeCountPosition!.Lotes);

        var invalidCountPayloads = new object[]
        {
            new
            {
                LoteId = lote.LoteId,
                QuantidadeVendavel = 3,
                QuantidadeDanificada = 1,
                QuantidadeQuarentena = 0,
                Motivo = "Versão ausente"
            },
            new
            {
                LoteId = lote.LoteId,
                QuantidadeVendavel = 3,
                QuantidadeDanificada = 1,
                QuantidadeQuarentena = 0,
                Motivo = "Versão nula",
                VersaoSaldoEsperada = (string?)null
            },
            new
            {
                LoteId = lote.LoteId,
                QuantidadeVendavel = 3,
                QuantidadeDanificada = 1,
                QuantidadeQuarentena = 0,
                Motivo = "Versão vazia",
                VersaoSaldoEsperada = string.Empty
            },
            new
            {
                LoteId = lote.LoteId,
                QuantidadeVendavel = 3,
                QuantidadeDanificada = 1,
                QuantidadeQuarentena = 0,
                Motivo = "Base64 inválida",
                VersaoSaldoEsperada = "não-é-base64!"
            }
        };

        for (var index = 0; index < invalidCountPayloads.Length; index++)
        {
            using var invalidCountRequest = CreateWriteRequest(
                $"{basePath}/variantes/{variant.Id:D}/contagens",
                invalidCountPayloads[index],
                $"http-finished-invalid-count-{index}",
                csrf.RequestToken);
            using var invalidCountResponse = await admin.SendAsync(invalidCountRequest);
            await AssertValidationProblemAsync(invalidCountResponse);
        }

        using var historyBeforeCountResponse = await admin.GetAsync(
            $"{basePath}/variantes/{variant.Id:D}/movimentacoes?pagina=1&itensPorPagina=20");
        Assert.Equal(HttpStatusCode.OK, historyBeforeCountResponse.StatusCode);
        var historyBeforeCount = await historyBeforeCountResponse.Content
            .ReadFromJsonAsync<PaginaAdminProdutoAcabadoResponse<MovimentoProdutoAcabadoAdminResponse>>();
        Assert.NotNull(historyBeforeCount);
        Assert.Equal(4, historyBeforeCount!.TotalItens);
        Assert.Equal(4, historyBeforeCount.Itens.Count);

        var countRequestBody = new ContagemProdutoAcabadoAdminRequest
        {
            LoteId = lote.LoteId,
            QuantidadeVendavel = 3,
            QuantidadeDanificada = 1,
            QuantidadeQuarentena = 0,
            Motivo = "Contagem HTTP",
            VersaoSaldoEsperada = loteAntesContagem.VersaoSaldo
                ?? throw new InvalidOperationException("The lot balance version is missing.")
        };
        using var countRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/contagens",
            countRequestBody,
            "http-finished-count",
            csrf.RequestToken);
        using var countResponse = await admin.SendAsync(countRequest);
        Assert.Equal(HttpStatusCode.Created, countResponse.StatusCode);
        var countMovements = await countResponse.Content
            .ReadFromJsonAsync<MovimentoProdutoAcabadoAdminResponse[]>();
        Assert.NotNull(countMovements);
        Assert.Equal(2, countMovements!.Length);
        Assert.All(countMovements, movement => Assert.Equal(lote.Codigo, movement.CodigoLote));

        using var countReplayRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/contagens",
            countRequestBody,
            "http-finished-count",
            csrf.RequestToken);
        using var countReplayResponse = await admin.SendAsync(countReplayRequest);
        Assert.Equal(HttpStatusCode.OK, countReplayResponse.StatusCode);
        var countReplay = await countReplayResponse.Content
            .ReadFromJsonAsync<ContagemProdutoAcabadoAdminResponse>();
        Assert.NotNull(countReplay);
        Assert.True(countReplay!.Repetida);
        Assert.Equal(countMovements.Select(movement => movement.Id), countReplay.Movimentos.Select(movement => movement.Id));
        Assert.All(countReplay.Movimentos, movement => Assert.Equal(lote.Codigo, movement.CodigoLote));

        var staleCountRequestBody = countRequestBody with
        {
            QuantidadeVendavel = 2,
            Motivo = "Contagem com versão desatualizada"
        };
        using var staleCountRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/contagens",
            staleCountRequestBody,
            "http-finished-stale-count",
            csrf.RequestToken);
        using var staleCountResponse = await admin.SendAsync(staleCountRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleCountResponse.StatusCode);

        using var entryReversalRequest = CreateWriteRequest(
            $"{basePath}/variantes/{variant.Id:D}/movimentacoes/{entry.Id:D}/estorno",
            new EstornoProdutoAcabadoAdminRequest { Motivo = "Estorno HTTP" },
            "http-finished-entry-reversal",
            csrf.RequestToken);
        using var entryReversalResponse = await admin.SendAsync(entryReversalRequest);
        Assert.Equal(HttpStatusCode.Conflict, entryReversalResponse.StatusCode);

        using var historyResponse = await admin.GetAsync(
            $"{basePath}/variantes/{variant.Id:D}/movimentacoes?pagina=1&itensPorPagina=20");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content
            .ReadFromJsonAsync<PaginaAdminProdutoAcabadoResponse<MovimentoProdutoAcabadoAdminResponse>>();
        Assert.NotNull(history);
        Assert.Equal(6, history!.TotalItens);
        Assert.Equal(6, history.Itens.Count);
        Assert.Equal(history.Itens.Count, history.Itens.Select(movement => movement.Id).Distinct().Count());
        Assert.All(history.Itens, movement => Assert.Equal(lote.Codigo, movement.CodigoLote));
        Assert.All(history.Itens, movement => Assert.Equal(AdminId, movement.ResponsavelId));
    }

    private sealed record CsrfState(string RequestToken);

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return new CsrfState(json.RootElement.GetProperty("requestToken").GetString()!);
    }

    private static HttpRequestMessage CreateWriteRequest(
        string uri,
        object body,
        string idempotencyKey,
        string requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, body.GetType())
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, requestToken);
        return request;
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("request", out var messages));
        Assert.True(messages.GetArrayLength() > 0);
    }

    private static WebApplicationFactory<Program> CreateFactory(SqlServerTestDatabase database)
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
                    ["Estoque:Calendario:TimeZoneId"] = "America/Sao_Paulo",
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(options =>
                    options.UseSqlServer(connectionString));
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

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        string? role = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, AdminId.ToString("D"));
        }

        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestSchemeName = "FinishedGoodsStockHttpIntegrationTest";
        public const string RoleHeader = "X-Test-Role";
        public const string UserIdHeader = "X-Test-User-Id";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role)
                || !Request.Headers.TryGetValue(UserIdHeader, out var userId)
                || !Guid.TryParse(userId, out var parsedUserId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, parsedUserId.ToString("D")),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                TestSchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName)));
        }
    }
}
