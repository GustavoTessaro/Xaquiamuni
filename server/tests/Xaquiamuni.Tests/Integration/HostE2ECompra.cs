using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Pedidos;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

internal sealed class HostE2ECompra : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _baseFactory = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly X509Certificate2 _certificado;

    public HostE2ECompra(SqlServerTestDatabase banco, string angular, string temporario, RelogioJornadaCompra? relogio = null,
        bool promocoesComerciais = false, TimeProvider? relogioAplicacao = null)
    {
        var alvo = new AlvoSqlE2E(banco.NomeBanco);
        alvo.ValidarConfiguracao(banco.ConexaoIsolada);

        using var chave = RSA.Create(2048);
        var req = new CertificateRequest("CN=Xaquiamuni E2E", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var nomes = new SubjectAlternativeNameBuilder();
        nomes.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(nomes.Build());
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false));
        using var certificado = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));
        // Schannel requires a key imported through the Windows certificate provider.
        var pfx = certificado.Export(X509ContentType.Pfx);
        try
        {
            _certificado = X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
        PinSpki = Convert.ToBase64String(SHA256.HashData(chave.ExportSubjectPublicKeyInfo()));

        var configuracao = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = banco.ConexaoIsolada,
            ["ExpiracaoPedidosWorker:Enabled"] = "false",
            ["EmailOutboxWorker:Enabled"] = "false",
            ["ResendEmail:Enabled"] = "false",
            ["PromocoesComerciais:Habilitadas"] = promocoesComerciais.ToString(),
            ["ProductImages:Storage:RootPath"] = Path.Combine(temporario, "imagens"),
            ["CpfProtection:CurrentEncryptionKeyVersion"] = "e2e",
            ["CpfProtection:EncryptionKeys:e2e"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["CpfProtection:CurrentFingerprintKeyVersion"] = "e2e",
            ["CpfProtection:FingerprintKeys:e2e"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };

        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(configuracao);
            });
            builder.ConfigureTestServices(services =>
            {
                if (relogioAplicacao is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(relogioAplicacao);
                }
                services.RemoveAll<IDbContextOptionsConfiguration<XaquiamuniDbContext>>();
                services.AddDbContext<XaquiamuniDbContext>(options =>
                {
                    options.UseSqlServer(banco.ConexaoIsolada).AddInterceptors(alvo);
                    if (relogio is not null) options.AddInterceptors(relogio);
                });
                services.AddScoped<EmailConfirmationOutboxProcessor>();
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    var descriptor = services[i];
                    if (descriptor.ServiceType == typeof(IHostedService) &&
                        (descriptor.ImplementationType == typeof(ExpiracaoPedidosBackgroundService)
                            || descriptor.ImplementationType == typeof(EmailOutboxBackgroundService)))
                    {
                        services.RemoveAt(i);
                    }
                }
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<CaixaEmailE2E>();
                services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CaixaEmailE2E>());
                services.AddSingleton<IStartupFilter>(new VerificarAlvoEServirAngular(alvo, angular));
            });
        });
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(_certificado)));
    }

    public string PinSpki { get; }
    public IServiceProvider Services => _factory.Services;
    public string Origin => Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    public async Task<ContaE2E> CriarContaAsync(string papel)
    {
        await using var scope = Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<XaquiamuniUser>>();
        if (!await roles.RoleExistsAsync(papel))
        {
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(papel))).Succeeded);
        }
        var id = Guid.NewGuid();
        var email = $"e2e-{id:N}@example.test";
        var senha = $"E2e!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}a1";
        var user = new XaquiamuniUser { Id = id, UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, senha)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, papel)).Succeeded);

        if (papel == XaquiamuniRoles.Cliente)
        {
            var context = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
            var cpf = new CpfProtegido(Convert.ToBase64String(RandomNumberGenerator.GetBytes(11)),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "e2e", "e2e");
            context.ClientePerfis.Add(ClientePerfil.Criar(id, "Cliente sintético E2E", cpf));
            await context.SaveChangesAsync();
        }

        return new(id, email, senha);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _baseFactory.DisposeAsync();
        }
        finally
        {
            _certificado.Dispose();
        }
    }

    private sealed class VerificarAlvoEServirAngular(AlvoSqlE2E alvo, string angular) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
                var conexao = db.Database.GetDbConnection();
                alvo.ValidarConfiguracao(conexao.ConnectionString);
                db.Database.OpenConnection();
                db.Database.CloseConnection();
            }

            var arquivos = new PhysicalFileProvider(angular);
            app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(arquivos.Dispose);
            app.Use(async (context, continuar) =>
            {
                var path = context.Request.Path;
                if (HttpMethods.IsGet(context.Request.Method) && !path.StartsWithSegments("/api")
                    && path != "/health" && !Path.HasExtension(path.Value))
                {
                    context.Request.Path = "/index.html";
                }

                await continuar();
            });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = arquivos });
            next(app);
        };
    }
}

internal sealed class ContaE2E(Guid id, string email, string senha)
{
    public Guid Id { get; } = id;
    public string Email { get; } = email;
    public string Senha { get; } = senha;
}

internal sealed class CaixaEmailE2E : IEmailSender
{
    internal ConcurrentQueue<EmailMessage> Mensagens { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Mensagens.Enqueue(message);
        return Task.CompletedTask;
    }
}
