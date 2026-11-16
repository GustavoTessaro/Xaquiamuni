using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo.Imagens;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public sealed class AdminProdutoImagemEndpointTests
{
    [Fact]
    public async Task ImageRoutes_RequireAuthenticatedAdministrator()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var path = $"{AdminProdutoImagemEndpointsPath()}/{Guid.NewGuid():D}/imagens";

        using var anonymous = await client.GetAsync(path);
        using var customer = await SendAsRoleAsync(client, path, XaquiamuniRoles.Cliente);
        using var employee = await SendAsRoleAsync(client, path, XaquiamuniRoles.Funcionario);
        using var admin = await SendAsRoleAsync(client, path, XaquiamuniRoles.Administrador);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task OpenApi_ListsImageRoutesAndMultipartContract()
    {
        using var factory = CreateFactory(new FakeGerenciarImagensProduto(), Environments.Development);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(
            "/api/admin/produtos/{produtoId}/imagens",
            out var imagesPath));
        Assert.True(paths.TryGetProperty(
            "/api/admin/produtos/{produtoId}/imagens/{imagemId}/conteudo",
            out _));
        Assert.True(imagesPath.GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .TryGetProperty("multipart/form-data", out _));
        Assert.True(paths.TryGetProperty(
            "/api/admin/produtos/{produtoId}/imagens/{imagemId}/variantes/{varianteId}",
            out _));
    }

    [Fact]
    public async Task ImageMutations_RequireAntiforgeryBeforeCallingApplication()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var root = $"{AdminProdutoImagemEndpointsPath()}/{Guid.NewGuid():D}/imagens";
        var imageId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Post, root)
            {
                Content = new MultipartFormDataContent()
            },
            new HttpRequestMessage(HttpMethod.Put, $"{root}/{imageId:D}/principal"),
            new HttpRequestMessage(HttpMethod.Put, $"{root}/ordem")
            {
                Content = JsonContent.Create(new ReordenarProdutoImagensRequest())
            },
            new HttpRequestMessage(HttpMethod.Put, $"{root}/{imageId:D}/variantes/{variantId:D}"),
            new HttpRequestMessage(HttpMethod.Delete, $"{root}/{imageId:D}/variantes/{variantId:D}"),
            new HttpRequestMessage(HttpMethod.Delete, $"{root}/{imageId:D}")
        };

        foreach (var request in requests)
        {
            using (request)
            {
                request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        Assert.Equal(0, manager.MutationCalls);

        using var invalidToken = CreateAdminRequest(HttpMethod.Put, $"{root}/{Guid.NewGuid():D}/principal");
        invalidToken.Headers.Add(AntiforgeryConfiguration.HeaderName, "invalid-request-token");
        invalidToken.Headers.Add("Cookie", csrf.CookiePair);
        using var invalidResponse = await client.SendAsync(invalidToken);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(0, manager.MutationCalls);
    }

    [Fact]
    public async Task Upload_UsesMultipartMetadataAndReturnsSafeCreatedResource()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var productId = Guid.NewGuid();
        var firstVariantId = Guid.NewGuid();
        var secondVariantId = Guid.NewGuid();
        using var request = CreateUploadRequest(
            productId,
            csrf,
            CreatePng(),
            "foto-sintetica.png",
            firstVariantId,
            secondVariantId);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ProdutoImagemResponse>();
        var serialized = JsonSerializer.Serialize(body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/admin/produtos/{productId:D}/imagens/{manager.Image.Id:D}", response.Headers.Location?.OriginalString);
        Assert.Equal(manager.Image.Id, body?.Id);
        Assert.Equal("image/webp", body?.TipoConteudo);
        Assert.Equal(
            $"/api/admin/produtos/{productId:D}/imagens/{manager.Image.Id:D}/conteudo",
            body?.ConteudoUrl);
        Assert.Equal(
            new[] { firstVariantId, secondVariantId },
            manager.LastInput?.VarianteIds);
        Assert.Equal(CreatePng().Length, manager.LastFileBytes?.Length);
        Assert.DoesNotContain("ChaveArmazenamento", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("physicalPath", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_RejectsMissingEmptyMultipleOversizedAndMalformedFormInputs()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var productId = Guid.NewGuid();

        using var missing = await SendFormAsync(
            client,
            CreateUploadRequest(productId, csrf, null, null));
        using var empty = await SendFormAsync(
            client,
            CreateUploadRequest(productId, csrf, [], "empty.png"));
        using var multiple = await SendFormAsync(
            client,
            CreateUploadRequest(productId, csrf, CreatePng(), "one.png", includeSecondFile: true));
        using var tooLarge = await SendFormAsync(
            client,
            CreateUploadRequest(
                productId,
                csrf,
                new byte[AdminProdutoImagemEndpoints.TamanhoMaximoArquivoBytes + 1],
                "oversized.png"));
        using var invalidVariant = await SendFormAsync(
            client,
            CreateUploadRequest(productId, csrf, CreatePng(), "valid.png", invalidVariantId: true));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, multiple.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidVariant.StatusCode);
        Assert.Equal(0, manager.MutationCalls);
    }

    [Fact]
    public async Task Upload_MapsInvalidImageAndDomainConflict()
    {
        var manager = new FakeGerenciarImagensProduto
        {
            OperationResult = ProdutoImagemOperacaoResult.InvalidImage("Conteúdo não reconhecido.")
        };
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var productId = Guid.NewGuid();

        using var invalidImage = await SendFormAsync(
            client,
            CreateUploadRequest(productId, csrf, [1, 2, 3], "not-an-image.bin"));
        manager.OperationResult = ProdutoImagemOperacaoResult.Conflict("Limite atingido.");
        using var conflict = await SendFormAsync(
            client,
            CreateUploadRequest(productId, csrf, CreatePng(), "limit.png"));

        Assert.Equal(HttpStatusCode.BadRequest, invalidImage.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(2, manager.MutationCalls);
    }

    [Fact]
    public async Task Upload_RejectsNonMultipartAndOverLimitMultipartRequest()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var path = $"{AdminProdutoImagemEndpointsPath()}/{Guid.NewGuid():D}/imagens";

        using var unsupported = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("not multipart")
        };
        AddAdministratorAndCsrf(unsupported, csrf);
        using var unsupportedResponse = await client.SendAsync(unsupported);

        using var overLimit = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(
                new byte[AdminProdutoImagemEndpoints.TamanhoMaximoRequisicaoBytes + 1])
        };
        overLimit.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data")
        {
            Parameters = { new NameValueHeaderValue("boundary", "\"test-boundary\"") }
        };
        AddAdministratorAndCsrf(overLimit, csrf);
        using var overLimitResponse = await client.SendAsync(overLimit);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupportedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, overLimitResponse.StatusCode);
        Assert.Equal(0, manager.MutationCalls);
    }

    [Fact]
    public async Task ImageOperations_MapFailuresAndReturnOrderedSafeMetadata()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var productId = Guid.NewGuid();
        var root = $"{AdminProdutoImagemEndpointsPath()}/{productId:D}/imagens";

        manager.OperationResult = ProdutoImagemOperacaoResult.Success(
            imagens:
            [
                manager.Image with { OrdemExibicao = 0 },
                manager.Image with { Id = Guid.NewGuid(), OrdemExibicao = 1, Principal = false }
            ]);
        using var listRequest = CreateAdminRequest(HttpMethod.Get, root);
        using var listResponse = await client.SendAsync(listRequest);
        var listed = await listResponse.Content.ReadFromJsonAsync<ProdutoImagemResponse[]>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var imageList = Assert.IsType<ProdutoImagemResponse[]>(listed);
        Assert.Equal(new[] { 0, 1 }, imageList.Select(image => image.OrdemExibicao));
        Assert.All(imageList, image =>
            Assert.EndsWith("/conteudo", image.ConteudoUrl, StringComparison.Ordinal));

        manager.OperationResult = ProdutoImagemOperacaoResult.ProductNotFound();
        using var missingRequest = CreateAdminRequest(HttpMethod.Get, root);
        using var missingResponse = await client.SendAsync(missingRequest);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        manager.OperationResult = ProdutoImagemOperacaoResult.Conflict("Limite atingido.");
        using var conflictRequest = CreateAdminRequest(HttpMethod.Put, $"{root}/{manager.Image.Id:D}/principal");
        AddAntiforgery(conflictRequest, await GetCsrfAsync(client));
        using var conflictResponse = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        manager.OperationResult = ProdutoImagemOperacaoResult.CommitOutcomeUnknown();
        using var unknownRequest = CreateAdminRequest(HttpMethod.Delete, $"{root}/{manager.Image.Id:D}");
        AddAntiforgery(unknownRequest, await GetCsrfAsync(client));
        using var unknownResponse = await client.SendAsync(unknownRequest);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unknownResponse.StatusCode);
        var unknownProblem = await unknownResponse.Content.ReadAsStringAsync();
        Assert.Contains("Não repita", unknownProblem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Content_StreamsWebpWithSafeHeadersAndDisposesTheStream()
    {
        var manager = new FakeGerenciarImagensProduto();
        var content = new TrackingStream([1, 2, 3, 4]);
        manager.ContentResult = ProdutoImagemConteudoResult.Success(manager.Image, content);
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        using var request = CreateAdminRequest(
            HttpMethod.Get,
            $"{AdminProdutoImagemEndpointsPath()}/{manager.Image.ProdutoId:D}/imagens/{manager.Image.Id:D}/conteudo");

        using var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
        Assert.True(content.WasDisposed);

        manager.ContentResult = ProdutoImagemConteudoResult.StoredFileMissing();
        using var missingRequest = CreateAdminRequest(
            HttpMethod.Get,
            $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/{manager.Image.Id:D}/conteudo");
        using var missingResponse = await client.SendAsync(missingRequest);
        var missingProblem = await missingResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Contains("arquivo físico", missingProblem, StringComparison.OrdinalIgnoreCase);

        manager.ContentResult = ProdutoImagemConteudoResult.StorageUnavailable();
        using var unavailableRequest = CreateAdminRequest(
            HttpMethod.Get,
            $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/{manager.Image.Id:D}/conteudo");
        using var unavailableResponse = await client.SendAsync(unavailableRequest);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
    }

    [Fact]
    public async Task VariantLinks_AreExposedAsIndividualAtomicMutations()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var variantId = Guid.NewGuid();
        var path = $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/{manager.Image.Id:D}"
            + $"/variantes/{variantId:D}";
        using var associate = CreateAdminRequest(HttpMethod.Put, path);
        AddAntiforgery(associate, csrf);
        using var associated = await client.SendAsync(associate);
        using var disassociate = CreateAdminRequest(HttpMethod.Delete, path);
        AddAntiforgery(disassociate, csrf);
        using var disassociated = await client.SendAsync(disassociate);

        Assert.Equal(HttpStatusCode.OK, associated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disassociated.StatusCode);
        Assert.Equal(1, manager.AssociationCalls);
        Assert.Equal(1, manager.DisassociationCalls);
    }

    [Fact]
    public async Task PrimaryAndOrderRoutes_ForwardExistingImageIdsToApplication()
    {
        var manager = new FakeGerenciarImagensProduto();
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        var imageIds = new[] { manager.Image.Id, Guid.NewGuid() };
        manager.OperationResult = ProdutoImagemOperacaoResult.Success(
            imagens:
            [
                manager.Image,
                manager.Image with { Id = imageIds[1], OrdemExibicao = 1, Principal = false }
            ]);

        using var primaryRequest = CreateAdminRequest(
            HttpMethod.Put,
            $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/{manager.Image.Id:D}/principal");
        AddAntiforgery(primaryRequest, csrf);
        using var primaryResponse = await client.SendAsync(primaryRequest);

        using var reorderRequest = CreateAdminRequest(
            HttpMethod.Put,
            $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/ordem");
        reorderRequest.Content = JsonContent.Create(new ReordenarProdutoImagensRequest
        {
            ImagemIds = imageIds
        });
        AddAntiforgery(reorderRequest, csrf);
        using var reorderResponse = await client.SendAsync(reorderRequest);

        Assert.Equal(HttpStatusCode.OK, primaryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reorderResponse.StatusCode);
        Assert.Equal(manager.Image.Id, manager.LastPrimaryImageId);
        Assert.Equal(imageIds, manager.LastReorderImageIds);

        manager.OperationResult = ProdutoImagemOperacaoResult.ValidationFailed(
            "imagemIds",
            "A lista deve conter todas as imagens.");
        using var invalidOrderRequest = CreateAdminRequest(
            HttpMethod.Put,
            $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/ordem");
        invalidOrderRequest.Content = JsonContent.Create(new ReordenarProdutoImagensRequest
        {
            ImagemIds = [imageIds[0], imageIds[0]]
        });
        AddAntiforgery(invalidOrderRequest, csrf);
        using var invalidOrderResponse = await client.SendAsync(invalidOrderRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrderResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_CleanupPendingReportsLogicalRemovalWithoutClaimingPhysicalDeletion()
    {
        var manager = new FakeGerenciarImagensProduto
        {
            OperationResult = ProdutoImagemOperacaoResult.CleanupPending()
        };
        using var factory = CreateFactory(manager);
        using var client = CreateClient(factory);
        var csrf = await GetCsrfAsync(client);
        using var request = CreateAdminRequest(
            HttpMethod.Delete,
            $"/api/admin/produtos/{manager.Image.ProdutoId:D}/imagens/{manager.Image.Id:D}");
        AddAntiforgery(request, csrf);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ProdutoImagemRemocaoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body?.RemovidaDoCatalogo);
        Assert.False(body?.LimpezaFisicaConcluida);
        Assert.Contains("pendente", body?.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    private static string AdminProdutoImagemEndpointsPath() => AdminProdutoEndpoints.BasePath;

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGerenciarImagensProduto manager,
        string environment = "Testing") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
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
                services.RemoveAll<IGerenciarImagensProduto>();
                services.AddSingleton<IGerenciarImagensProduto>(manager);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    private static async Task<HttpResponseMessage> SendAsRoleAsync(
        HttpClient client,
        string path,
        string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateAdminRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        return request;
    }

    private static HttpRequestMessage CreateUploadRequest(
        Guid productId,
        CsrfState csrf,
        byte[]? fileBytes,
        string? fileName,
        Guid firstVariantId = default,
        Guid secondVariantId = default,
        bool includeSecondFile = false,
        bool invalidVariantId = false)
    {
        var request = CreateAdminRequest(
            HttpMethod.Post,
            $"{AdminProdutoImagemEndpointsPath()}/{productId:D}/imagens");
        var form = new MultipartFormDataContent();
        if (fileBytes is not null)
        {
            form.Add(
                CreateFilePart(fileBytes),
                AdminProdutoImagemEndpoints.CampoArquivo,
                fileName ?? throw new ArgumentNullException(nameof(fileName)));
            if (includeSecondFile)
            {
                form.Add(CreateFilePart(CreatePng()), AdminProdutoImagemEndpoints.CampoArquivo, "second.png");
            }
        }

        if (firstVariantId != Guid.Empty)
        {
            form.Add(new StringContent(firstVariantId.ToString("D")), AdminProdutoImagemEndpoints.CampoVarianteId);
        }

        if (secondVariantId != Guid.Empty)
        {
            form.Add(new StringContent(secondVariantId.ToString("D")), AdminProdutoImagemEndpoints.CampoVarianteId);
        }

        if (invalidVariantId)
        {
            form.Add(new StringContent("not-a-guid"), AdminProdutoImagemEndpoints.CampoVarianteId);
        }

        request.Content = form;
        AddAntiforgery(request, csrf);
        return request;
    }

    private static ByteArrayContent CreateFilePart(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return part;
    }

    private static async Task<HttpResponseMessage> SendFormAsync(
        HttpClient client,
        HttpRequestMessage request) =>
        await client.SendAsync(request);

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AntiforgeryConfiguration.TokenEndpoint);
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = Assert.IsType<string>(document.RootElement.GetProperty("requestToken").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        return new CsrfState(requestToken, cookie.Split(';', 2)[0]);
    }

    private static void AddAntiforgery(HttpRequestMessage request, CsrfState csrf)
    {
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        request.Headers.Add("Cookie", csrf.CookiePair);
    }

    private static void AddAdministratorAndCsrf(HttpRequestMessage request, CsrfState csrf)
    {
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, XaquiamuniRoles.Administrador);
        AddAntiforgery(request, csrf);
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(8, 6);
        bitmap.Erase(SkiaSharp.SKColors.CadetBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private sealed record CsrfState(string RequestToken, string CookiePair);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestSchemeName = "ProductImageEndpointTest";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "product-image-test-user"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                TestSchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName)));
        }
    }

    private sealed class FakeGerenciarImagensProduto : IGerenciarImagensProduto
    {
        public ProdutoImagemDto Image { get; } = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "sintetica.png",
            "image/webp",
            4,
            8,
            6,
            0,
            true,
            [],
            DateTimeOffset.UtcNow);

        public ProdutoImagemOperacaoResult OperationResult { get; set; }
            = ProdutoImagemOperacaoResult.Success();
        public ProdutoImagemConteudoResult ContentResult { get; set; }
            = ProdutoImagemConteudoResult.StoredFileMissing();
        public int MutationCalls { get; private set; }
        public int AssociationCalls { get; private set; }
        public int DisassociationCalls { get; private set; }
        public Guid? LastPrimaryImageId { get; private set; }
        public IReadOnlyList<Guid>? LastReorderImageIds { get; private set; }
        public AdicionarImagemProdutoInput? LastInput { get; private set; }
        public byte[]? LastFileBytes { get; private set; }

        public async Task<ProdutoImagemOperacaoResult> AdicionarAsync(
            Guid produtoId,
            AdicionarImagemProdutoInput? input,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            LastInput = input;
            if (input?.Arquivo is not null)
            {
                using var output = new MemoryStream();
                await input.Arquivo.CopyToAsync(output, cancellationToken);
                LastFileBytes = output.ToArray();
            }

            return OperationResult.Status == ProdutoImagemOperacaoStatus.Success
                ? ProdutoImagemOperacaoResult.Success(Image with { ProdutoId = produtoId })
                : OperationResult;
        }

        public Task<ProdutoImagemOperacaoResult> ListarAsync(
            Guid produtoId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult);

        public Task<ProdutoImagemOperacaoResult> ConsultarAsync(
            Guid produtoId,
            Guid imagemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult);

        public Task<ProdutoImagemConteudoResult> AbrirConteudoAsync(
            Guid produtoId,
            Guid imagemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ContentResult);

        public Task<ProdutoImagemConteudoResult> AbrirConteudoPublicoAsync(
            Guid produtoId,
            Guid imagemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ContentResult);

        public Task<ProdutoImagemOperacaoResult> DefinirPrincipalAsync(
            Guid produtoId,
            Guid imagemId,
            CancellationToken cancellationToken = default)
        {
            LastPrimaryImageId = imagemId;
            return MutationResult();
        }

        public Task<ProdutoImagemOperacaoResult> ReordenarAsync(
            Guid produtoId,
            IReadOnlyList<Guid>? imagemIds,
            CancellationToken cancellationToken = default)
        {
            LastReorderImageIds = imagemIds;
            return MutationResult();
        }

        public Task<ProdutoImagemOperacaoResult> AssociarVarianteAsync(
            Guid produtoId,
            Guid imagemId,
            Guid varianteId,
            CancellationToken cancellationToken = default)
        {
            AssociationCalls++;
            return MutationResult();
        }

        public Task<ProdutoImagemOperacaoResult> DesassociarVarianteAsync(
            Guid produtoId,
            Guid imagemId,
            Guid varianteId,
            CancellationToken cancellationToken = default)
        {
            DisassociationCalls++;
            return MutationResult();
        }

        public Task<ProdutoImagemOperacaoResult> RemoverAsync(
            Guid produtoId,
            Guid imagemId,
            CancellationToken cancellationToken = default) =>
            MutationResult();

        private Task<ProdutoImagemOperacaoResult> MutationResult()
        {
            MutationCalls++;
            return Task.FromResult(OperationResult);
        }
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
