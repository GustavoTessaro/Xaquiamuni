using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AdminProdutoImagemSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task AdminHttp_CriaProdutoEnviaLePublicaERemoveImagemComSqlEFilesystemDescartaveis()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var storage = new TemporaryImageStorage();
        using var factory = CreateFactory(database, storage.RootPath);
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.RoleHeader,
            XaquiamuniRoles.Administrador);

        Guid categoryId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
            var category = Categoria.Criar("Imagens HTTP descartáveis", null, 0);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();
            categoryId = category.Id;
        }

        var csrf = await GetCsrfAsync(client);
        using var createProductRequest = new HttpRequestMessage(
            HttpMethod.Post,
            AdminProdutoEndpoints.BasePath)
        {
            Content = JsonContent.Create(new ProdutoCadastroRequest
            {
                Nome = "Produto de teste HTTP",
                CategoriaId = categoryId,
                Simples = true,
                VariantePadrao = new ProdutoVarianteRequest
                {
                    UnidadeVenda = "UN",
                    PrecoVenda = 20m
                }
            })
        };
        AddAntiforgery(createProductRequest, csrf);
        using var createProductResponse = await client.SendAsync(createProductRequest);
        Assert.Equal(HttpStatusCode.Created, createProductResponse.StatusCode);
        var product = await createProductResponse.Content.ReadFromJsonAsync<ProdutoResponse>();
        var productId = Assert.IsType<ProdutoResponse>(product).Id;
        var imagePath = $"{AdminProdutoEndpoints.BasePath}/{productId:D}/imagens";

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, imagePath);
        var form = new MultipartFormDataContent();
        var imagePart = new ByteArrayContent(CreateSyntheticPng());
        imagePart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(imagePart, AdminProdutoImagemEndpoints.CampoArquivo, "synthetic.png");
        uploadRequest.Content = form;
        AddAntiforgery(uploadRequest, csrf);
        using var uploadResponse = await client.SendAsync(uploadRequest);
        var image = await uploadResponse.Content.ReadFromJsonAsync<ProdutoImagemResponse>();
        var imageDto = Assert.IsType<ProdutoImagemResponse>(image);

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        Assert.Equal("image/webp", imageDto.TipoConteudo);
        Assert.True(imageDto.Principal);
        Assert.NotNull(uploadResponse.Headers.Location);
        Assert.DoesNotContain(
            "ChaveArmazenamento",
            System.Text.Json.JsonSerializer.Serialize(imageDto),
            StringComparison.OrdinalIgnoreCase);

        using var listResponse = await client.GetAsync(imagePath);
        using var singleResponse = await client.GetAsync($"{imagePath}/{imageDto.Id:D}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, singleResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ProdutoImagemResponse[]>();
        Assert.Equal(imageDto.Id, Assert.Single(list!).Id);
        var single = await singleResponse.Content.ReadFromJsonAsync<ProdutoImagemResponse>();
        Assert.Equal(imageDto.Id, single?.Id);

        using var contentResponse = await client.GetAsync(imageDto.ConteudoUrl);
        var webp = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("image/webp", contentResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(contentResponse.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(webp, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(webp, 8, 4));

        using var publishRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{AdminProdutoEndpoints.BasePath}/{productId:D}/publicacao")
        {
            Content = JsonContent.Create(new ProdutoPublicacaoRequest { Publicado = true })
        };
        AddAntiforgery(publishRequest, csrf);
        using var publishResponse = await client.SendAsync(publishRequest);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        using var blockedDeleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{imagePath}/{imageDto.Id:D}");
        AddAntiforgery(blockedDeleteRequest, csrf);
        using var blockedDeleteResponse = await client.SendAsync(blockedDeleteRequest);
        Assert.Equal(HttpStatusCode.Conflict, blockedDeleteResponse.StatusCode);

        using var unpublishRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{AdminProdutoEndpoints.BasePath}/{productId:D}/publicacao")
        {
            Content = JsonContent.Create(new ProdutoPublicacaoRequest { Publicado = false })
        };
        AddAntiforgery(unpublishRequest, csrf);
        using var unpublishResponse = await client.SendAsync(unpublishRequest);
        Assert.Equal(HttpStatusCode.OK, unpublishResponse.StatusCode);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{imagePath}/{imageDto.Id:D}");
        AddAntiforgery(deleteRequest, csrf);
        using var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Empty(Directory.Exists(storage.ImagesPath)
            ? Directory.GetFiles(storage.ImagesPath)
            : []);

        await using var verification = database.CreateVerificationContext();
        Assert.False(await verification.ProdutoImagens.AnyAsync(item => item.Id == imageDto.Id));
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

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = Assert.IsType<string>(document.RootElement.GetProperty("requestToken").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        return new CsrfState(requestToken, cookie.Split(';', 2)[0]);
    }

    private static void AddAntiforgery(HttpRequestMessage request, CsrfState csrf)
    {
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        request.Headers.Add("Cookie", csrf.CookiePair);
    }

    private static byte[] CreateSyntheticPng()
    {
        using var bitmap = new SKBitmap(32, 24);
        bitmap.Erase(SKColors.MediumPurple);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private sealed record CsrfState(string RequestToken, string CookiePair);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestSchemeName = "ProductImageHttpIntegrationTest";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "product-image-http-integration-user"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                TestSchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName)));
        }
    }

    private sealed class TemporaryImageStorage : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"xaquiamuni-image-http-{Guid.NewGuid():N}");

        public string ImagesPath => Path.Combine(RootPath, "product-images");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
