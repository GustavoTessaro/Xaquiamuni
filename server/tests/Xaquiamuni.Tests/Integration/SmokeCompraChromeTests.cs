using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Pedidos;
using Xunit.Abstractions;

namespace Xaquiamuni.Tests.Integration;

public sealed class SmokeCompraChromeTests(ITestOutputHelper output)
{
    private const string TemporarioPrefixo = "xaquiamuni-compra-e2e-";

    [Theory]
    [InlineData(@".\SQLEXPRESS", "XaquiamuniDev")]
    [InlineData("outro.database.windows.net", "owned")]
    [InlineData(@".\SQLEXPRESS", "other")]
    public void RecusaAlvoQueNaoFoiCriadoPelaExecucao(string servidor, string banco)
    {
        var esperado = $"Xaquiamuni_IntegrationTest_{Guid.NewGuid():N}";
        var conexao = new SqlConnectionStringBuilder
        {
            DataSource = servidor,
            InitialCatalog = banco switch { "owned" => esperado, "other" => $"Xaquiamuni_IntegrationTest_{Guid.NewGuid():N}", _ => banco },
            IntegratedSecurity = true,
        };
        Assert.Throws<InvalidOperationException>(() => new AlvoSqlE2E(esperado).ValidarConfiguracao(conexao.ConnectionString));
        conexao.DataSource = @".\SQLEXPRESS";
        conexao.InitialCatalog = esperado;
        new AlvoSqlE2E(esperado).ValidarConfiguracao(conexao.ConnectionString);
    }

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task SmokeChromeUsaAngularApiSqlReaisComSessoesSeparadas()
    {
        var repo = EncontrarRepositorio();
        var dist = Path.Combine(repo, "client", "dist", "xaquiamuni", "browser");
        var script = Path.Combine(repo, "server", "tests", "Xaquiamuni.Tests", "Integration", "chrome-compra-smoke.mjs");
        var chrome = Environment.GetEnvironmentVariable("XAQUIAMUNI_CHROME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
        Assert.True(File.Exists(Path.Combine(dist, "index.html")), "Gere o build Angular antes do smoke.");
        Assert.True(File.Exists(chrome), "Chrome não encontrado. Defina XAQUIAMUNI_CHROME para o executável instalado.");
        Assert.True(File.Exists(script), "Executor Chrome ausente.");

        var temporario = Directory.CreateTempSubdirectory(TemporarioPrefixo).FullName;
        try
        {
            string nomeBanco;
            string conexaoMaster;
            Uri endereco;
            await using (var db = await SqlServerTestDatabase.CreateAsync())
            {
                nomeBanco = db.NomeBanco;
                conexaoMaster = new SqlConnectionStringBuilder(db.ConexaoIsolada) { InitialCatalog = "master" }.ConnectionString;
                await using (var host = new HostE2ECompra(db, dist, temporario))
                {
                    endereco = new Uri(host.Origin);
                    Assert.Equal("https", endereco.Scheme);
                    Assert.True(endereco.IsLoopback);
                    Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), worker =>
                        worker is ExpiracaoPedidosBackgroundService or EmailOutboxBackgroundService);
                    var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
                    var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
                    var resultado = await RodarChromeAsync(script, new
                    {
                        origin = endereco.GetLeftPart(UriPartial.Authority), pinSpki = host.PinSpki,
                        chromePath = chrome, profile = Path.Combine(temporario, "chrome"), cliente, admin,
                    });
                    output.WriteLine(resultado);
                    Assert.Empty(host.Services.GetRequiredService<CaixaEmailE2E>().Mensagens);
                }

                using var socket = new TcpClient();
                using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await Assert.ThrowsAsync<SocketException>(async () =>
                    await socket.ConnectAsync(endereco.Host, endereco.Port, limite.Token));
            }

            await using var master = new SqlConnection(conexaoMaster);
            await master.OpenAsync();
            await using var command = master.CreateCommand();
            command.CommandText = "SELECT CASE WHEN DB_ID(@banco) IS NULL THEN 0 ELSE 1 END;";
            command.Parameters.AddWithValue("@banco", nomeBanco);
            Assert.Equal(0, await command.ExecuteScalarAsync());
            output.WriteLine("Host encerrado e base descartável removida.");
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }

    private static async Task<string> RodarChromeAsync(string script, object parametros)
    {
        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        start.ArgumentList.Add(script);
        using var processo = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o executor Chrome.");
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        try
        {
            var stdout = processo.StandardOutput.ReadToEndAsync(limite.Token);
            var stderr = processo.StandardError.ReadToEndAsync(limite.Token);
            await processo.StandardInput.WriteAsync(JsonSerializer.Serialize(parametros));
            processo.StandardInput.Close();
            await processo.WaitForExitAsync(limite.Token);
            if (processo.ExitCode != 0)
            {
                throw new InvalidOperationException("Falha no smoke Chrome: " + await stderr);
            }
            return await stdout;
        }
        finally
        {
            if (!processo.HasExited)
            {
                processo.Kill(entireProcessTree: true);
                await processo.WaitForExitAsync();
            }
        }
    }

    private static string EncontrarRepositorio()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "angular.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repositório Xaquiamuni não encontrado.");
    }

    private static void RemoverTemporario(string temporario)
    {
        var alvo = Path.GetFullPath(temporario);
        var raiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetDirectoryName(alvo), raiz, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(alvo).StartsWith(TemporarioPrefixo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Diretório de limpeza recusado: {alvo}");
        }

        try
        {
            Directory.Delete(alvo, recursive: true);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Diretório descartável pendente: {alvo}", exception);
        }
    }
}

internal sealed class ChromeCompraFactAttribute : FactAttribute
{
    public ChromeCompraFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("XAQUIAMUNI_RUN_CHROME_E2E") != "1"
            || Environment.GetEnvironmentVariable("XAQUIAMUNI_RUN_SQLSERVER_INTEGRATION") != "1")
        {
            Skip = "Habilite explicitamente o smoke Chrome e o SQL descartável.";
        }
    }
}
