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
    public Task SmokeChromeUsaAngularApiSqlReaisComSessoesSeparadas() => ExecutarChromeAsync(jornada: false);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task JornadaChromeCompraRetiradaComConciliacao() => ExecutarChromeAsync(jornada: true);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task JornadaChromePixManualComConciliacao() => ExecutarChromeAsync(jornada: true, pixManual: true);

    private async Task ExecutarChromeAsync(bool jornada, bool pixManual = false)
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
                var relogio = jornada ? new RelogioJornadaCompra() : null;
                await using (var host = new HostE2ECompra(db, dist, temporario, relogio))
                {
                    endereco = new Uri(host.Origin);
                    Assert.Equal("https", endereco.Scheme);
                    Assert.True(endereco.IsLoopback);
                    Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), worker =>
                        worker is ExpiracaoPedidosBackgroundService or EmailOutboxBackgroundService);
                    var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
                    var cliente = jornada
                        ? new ContaE2E(Guid.Empty, $"e2e-{Guid.NewGuid():N}@example.test", $"E2e!{Guid.NewGuid():N}a1")
                        : await host.CriarContaAsync(XaquiamuniRoles.Cliente);
                    var fluxo = relogio is null ? null : new JornadaCompraE2E(host, relogio, admin.Id, cliente.Email);
                    var outroCliente = jornada ? await host.CriarContaAsync(XaquiamuniRoles.Cliente) : null;
                    var seed = fluxo is null ? null : await fluxo.SemearAsync();
                    await RodarChromeAsync(script, new
                    {
                        origin = endereco.GetLeftPart(UriPartial.Authority), pinSpki = host.PinSpki,
                        chromePath = chrome, profile = Path.Combine(temporario, "chrome"), cliente, admin,
                        outroCliente, seed, pixManual,
                    }, fluxo);
                    Assert.Equal(jornada ? 1 : 0, host.Services.GetRequiredService<CaixaEmailE2E>().Mensagens.Count);
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

    private async Task RodarChromeAsync(string script, object parametros, JornadaCompraE2E? fluxo)
    {
        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        start.ArgumentList.Add(script);
        using var processo = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o executor Chrome.");
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(fluxo is null ? 80 : 180));
        try
        {
            var stderr = processo.StandardError.ReadToEndAsync(limite.Token);
            await processo.StandardInput.WriteLineAsync(JsonSerializer.Serialize(parametros));
            await processo.StandardInput.FlushAsync(limite.Token);
            string? linha;
            while ((linha = await processo.StandardOutput.ReadLineAsync(limite.Token)) is not null)
            {
                if (fluxo is not null && linha.StartsWith("@checkpoint ", StringComparison.Ordinal))
                {
                    var partes = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var etapa = partes[1];
                    output.WriteLine($"Checkpoint: {etapa}");
                    object resposta = true;
                    if (etapa == "email")
                    {
                        resposta = await fluxo.CapturarConfirmacaoAsync(limite.Token);
                    }
                    else
                    {
                        await fluxo.ConferirAsync(etapa, Guid.Parse(partes[2]), limite.Token);
                    }
                    await processo.StandardInput.WriteLineAsync(JsonSerializer.Serialize(resposta));
                    await processo.StandardInput.FlushAsync(limite.Token);
                }
                else
                {
                    output.WriteLine(linha);
                }
            }
            await processo.WaitForExitAsync(limite.Token);
            if (processo.ExitCode != 0)
            {
                throw new InvalidOperationException("Falha no Chrome: " + await stderr);
            }
        }
        finally
        {
            processo.StandardInput.Close();
            if (!processo.HasExited)
            {
                using var limpeza = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                try { await processo.WaitForExitAsync(limpeza.Token); }
                catch (OperationCanceledException) when (!processo.HasExited) { processo.Kill(entireProcessTree: true); }
            }
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
