using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Pedidos;
using Xaquiamuni.Infraestrutura.Persistencia;
using Microsoft.Extensions.Options;
using Xaquiamuni.Dominio.Estoque;
using Xunit.Abstractions;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests(ITestOutputHelper output)
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
    public Task ChromeAuditaSeisTelasAdministrativas() => ExecutarChromeAsync(jornada: false, auditoriaAdmin: true);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task ChromeRecuperaTentativaFinanceiraPersistida() => ExecutarChromeAsync(jornada: false, financeiro: true);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task ChromeAgendaAdministrativa() => ExecutarChromeAsync(jornada: false, agenda: true);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task ChromeModoAdminIsolaOsSeisTemas() =>
        ExecutarChromeAsync(jornada: false, financeiro: true, modoAdmin: true);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task JornadaChromePixManualComConciliacao() => ExecutarChromeAsync(jornada: true, pixManual: true);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task DemonstracaoIniciaAFrioComCatalogoEEncerra() => ExecutarDemonstracaoAsync(cnpj: false);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task DemonstracaoPixCnpjEHistoricoAteEntrega() => ExecutarDemonstracaoAsync(cnpj: true);

    private async Task ExecutarDemonstracaoAsync(bool cnpj)
    {
        string nomeBanco;
        string conexaoMaster;
        string temporario;
        Uri endereco;
        await using (var demo = await AmbienteDemonstracao.CriarAsync(DemonstracaoLocal.CaminhoAngular()))
        {
            nomeBanco = demo.NomeBanco;
            conexaoMaster = new SqlConnectionStringBuilder(demo.ConexaoIsolada) { InitialCatalog = "master" }.ConnectionString;
            temporario = demo.Temporario;
            endereco = new Uri(demo.Host.Origin);
            Assert.True(endereco.IsLoopback);
            Assert.Equal("https", endereco.Scheme);
            Assert.DoesNotContain(demo.Host.Services.GetServices<IHostedService>(), worker =>
                worker is ExpiracaoPedidosBackgroundService or EmailOutboxBackgroundService);
            Assert.Null(await demo.ProximaConfirmacaoAsync(CancellationToken.None));
            Assert.True(demo.Host.Services.GetRequiredService<IOptions<PromocoesComerciaisOptions>>().Value.Habilitadas);
            await using (var escopo = demo.Host.Services.CreateAsyncScope())
            {
                var database = escopo.ServiceProvider.GetRequiredService<XaquiamuniDbContext>().Database;
                Assert.Empty(await database.GetPendingMigrationsAsync());
                Assert.Contains("20261121094657_CriarPromocoes", await database.GetAppliedMigrationsAsync());
                Assert.Contains("20261121101248_RegistrarPrecoAceitoPedido", await database.GetAppliedMigrationsAsync());
            }
            var script = Path.Combine(EncontrarRepositorio(), "server", "tests", "Xaquiamuni.Tests", "Integration", "chrome-compra-smoke.mjs");
            var outroCliente = cnpj ? await demo.Host.CriarContaAsync(XaquiamuniRoles.Cliente) : null;
            await RodarChromeAsync(script, new
            {
                origin = demo.Host.Origin, pinSpki = demo.Host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome-validacao"), cliente = demo.Cliente, admin = demo.Admin,
                demonstracao = true, pixCnpj = cnpj, seed = cnpj ? demo.ProdutoPrincipal : null, outroCliente,
            }, cnpj ? demo.Jornada : null, demo);
        }

        Assert.False(Directory.Exists(temporario));
        using var socket = new TcpClient();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<SocketException>(async () =>
            await socket.ConnectAsync(endereco.Host, endereco.Port, limite.Token));
        await using var master = new SqlConnection(conexaoMaster);
        await master.OpenAsync();
        await using var comando = master.CreateCommand();
        comando.CommandText = "SELECT CASE WHEN DB_ID(@banco) IS NULL THEN 0 ELSE 1 END;";
        comando.Parameters.AddWithValue("@banco", nomeBanco);
        Assert.Equal(0, await comando.ExecuteScalarAsync());
        output.WriteLine("Demonstração encerrada: HTTPS indisponível, banco e diretório removidos.");
    }

    [ChromeCompraFact]
    [Trait("Categoria", "ChromeE2E")]
    public Task ChromeCorreiosManualConverteERegistraPostagem() => ExecutarChromeAsync(jornada: false, postal: true);

    private async Task ExecutarChromeAsync(bool jornada, bool pixManual = false, bool auditoriaAdmin = false,
        bool financeiro = false, bool agenda = false, bool modoAdmin = false, bool postal = false)
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
                await using (var host = new HostE2ECompra(db, dist, temporario, relogio,
                    municipiosPorCep: modoAdmin ? new Dictionary<string, string> { ["95200000"] = "4322509" } : null,
                    municipiosPostais: postal ? new Dictionary<string, MunicipioPostal> { ["95200000"] = new("4322509", "Vacaria", "RS") } : null))
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
                    Guid? varianteAuditoria = null;
                    Guid? insumoFinanceiro = null;
                    Guid? produtoFinanceiro = null;
                    Guid? varianteFinanceiro = null;
                    if (auditoriaAdmin || financeiro || postal)
                    {
                        await using var verificacao = db.CreateVerificationContext();
                        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(verificacao);
                        if (modoAdmin || postal)
                            verificacao.ClienteEnderecos.Add(ClienteEndereco.Criar(Guid.NewGuid(), cliente.Id,
                                "95200000", "RS", "Vacaria", "Centro", "Rua cadastrada do ensaio", "12", null, true));
                        if (auditoriaAdmin) varianteAuditoria = variante.Id;
                        if (financeiro || postal)
                        {
                            produtoFinanceiro = produto.Id;
                            varianteFinanceiro = variante.Id;
                        }
                        var insumo = InsumoEstoque.Criar("Base sintética",
                            TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
                        if (financeiro) insumoFinanceiro = insumo.Id;
                        verificacao.InsumosEstoque.Add(insumo);
                        await verificacao.SaveChangesAsync();
                    }
                    await RodarChromeAsync(script, new
                    {
                        origin = endereco.GetLeftPart(UriPartial.Authority), pinSpki = host.PinSpki,
                        chromePath = chrome, profile = Path.Combine(temporario, "chrome"), cliente, admin,
                        outroCliente, seed, pixManual, varianteAuditoria, insumoFinanceiro, produtoFinanceiro,
                        varianteFinanceiro, financeiro, agenda, modoAdmin, postal,
                    }, fluxo);
                    if (modoAdmin)
                    {
                        await using var verificacao = db.CreateVerificationContext();
                        var entregas = await verificacao.Pedidos.AsNoTracking()
                            .Where(p => p.ModalidadeEntrega == ModalidadeEntregaPedido.EntregaPropria).ToListAsync();
                        Assert.Equal(2, entregas.Count);
                        Assert.All(entregas, p =>
                        {
                            Assert.Equal(SituacaoPedido.Entregue, p.Situacao);
                            Assert.Equal((1199L, 1300L, 2499L), (p.SubtotalProdutosCentavos, p.FreteCentavos, p.TotalCentavos));
                            Assert.Equal("4322509", p.Entrega!.Destino!.CodigoIbge);
                            Assert.NotNull(p.SaidaEntregaEmUtc);
                        });
                        Assert.Contains(entregas, p => p.Entrega!.Destino!.Logradouro == "Rua cadastrada do ensaio");
                        Assert.Contains(entregas, p => p.Entrega!.Destino!.Logradouro == "Rua do ensaio");
                        var ids = entregas.Select(p => p.Id).ToArray();
                        var reservas = await verificacao.ReservasProdutoAcabado.AsNoTracking()
                            .Where(r => ids.Contains(r.PedidoId)).ToListAsync();
                        Assert.Equal(2, reservas.Count);
                        Assert.All(reservas, r => Assert.Equal(EstadoReservaProdutoAcabado.Consumida, r.Estado));
                        var reservaIds = reservas.Select(r => (Guid?)r.Id).ToArray();
                        Assert.Equal(2, await verificacao.MovimentosProdutoAcabado.CountAsync(m =>
                            reservaIds.Contains(m.ReservaProdutoAcabadoId)));
                        output.WriteLine("SQL: dois snapshots de 1199 + 1300 = 2499 centavos e uma baixa por entrega.");
                    }
                    if (postal)
                    {
                        await using var verificacao = db.CreateVerificationContext();
                        var pedidos = await verificacao.Pedidos.AsNoTracking().ToListAsync();
                        var enderecoCadastrado = await verificacao.ClienteEnderecos.AsNoTracking()
                            .SingleAsync(x => x.ClienteUserId == cliente.Id && x.IsDefault);
                        Assert.Equal(("Rua cadastrada do ensaio", "12"), (enderecoCadastrado.Logradouro, enderecoCadastrado.Numero));
                        Assert.All(pedidos, pedido => Assert.Equal("Rua de teste", pedido.Entrega!.Destino!.Logradouro));
                        Assert.Equal(new[] { "1280", "375" }, pedidos.Select(p => p.Entrega!.Destino!.Numero).Order());
                        Assert.Equal(2, pedidos.Count);
                        Assert.All(pedidos, pedido =>
                        {
                            Assert.Equal(ModalidadeEntregaPedido.Correios, pedido.ModalidadeEntrega);
                            Assert.Equal(SituacaoPedido.Entregue, pedido.Situacao);
                            Assert.Equal((1199L, 1290L, 2489L),
                                (pedido.SubtotalProdutosCentavos, pedido.FreteCentavos, pedido.TotalCentavos));
                            Assert.Equal("AB123456789BR", pedido.CodigoRastreamento);
                        });
                        var reservas = await verificacao.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
                        Assert.Equal(2, reservas.Count);
                        Assert.All(reservas, r => Assert.Equal(EstadoReservaProdutoAcabado.Consumida, r.Estado));
                        var reservaIds = reservas.Select(r => (Guid?)r.Id).ToArray();
                        Assert.Equal(2, await verificacao.MovimentosProdutoAcabado.CountAsync(m =>
                            reservaIds.Contains(m.ReservaProdutoAcabadoId)));
                    }
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

    private async Task RodarChromeAsync(string script, object parametros, JornadaCompraE2E? fluxo,
        AmbienteDemonstracao? demo = null, Func<string, Task<object>>? checkpoint = null)
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
                if ((fluxo is not null || demo is not null || checkpoint is not null) && linha.StartsWith("@checkpoint ", StringComparison.Ordinal))
                {
                    var partes = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var etapa = partes[1];
                    output.WriteLine($"Checkpoint: {etapa}");
                    object resposta = true;
                    if (checkpoint is not null)
                        resposta = await checkpoint(etapa);
                    else if (etapa == "email")
                    {
                        resposta = demo is not null
                            ? await demo.ProximaConfirmacaoAsync(limite.Token)
                                ?? throw new InvalidOperationException("Confirmação local não capturada.")
                            : await fluxo!.CapturarConfirmacaoAsync(limite.Token);
                    }
                    else
                    {
                        await fluxo!.ConferirAsync(etapa, Guid.Parse(partes[2]), limite.Token);
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
