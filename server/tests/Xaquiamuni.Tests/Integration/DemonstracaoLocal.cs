using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;

namespace Xaquiamuni.Tests.Integration;

public static class DemonstracaoLocal
{
    public static async Task<int> ExecutarAsync()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Execute a demonstração em um terminal interativo do Windows.");
            return 1;
        }

        using var cancelar = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelarConsole = (_, e) => { e.Cancel = true; cancelar.Cancel(); };
        Console.CancelKeyPress += cancelarConsole;
        try
        {
            var chrome = CaminhoChrome();
            if (!File.Exists(chrome))
                throw new InvalidOperationException("Chrome não encontrado. Ajuste XAQUIAMUNI_CHROME.");
            var angular = CaminhoAngular();
            await using var ambiente = await AmbienteDemonstracao.CriarAsync(angular);
            cancelar.Token.ThrowIfCancellationRequested();
            Console.WriteLine($"Demonstração: {ambiente.Host.Origin}");
            Console.WriteLine("Somente dados sintéticos. Não faça transferências.");
            Console.WriteLine("Copiar para a área de transferência: 1 e-mail ADMIN, 2 senha ADMIN,");
            Console.WriteLine("3 e-mail CLIENTE, 4 senha CLIENTE, 5 confirmação local, 6 limpar.");
            Console.WriteLine("Depois de colar, pressione 6. Para encerrar, pressione Q ou Ctrl+C.");

            using var navegadorCliente = AbrirChrome(chrome, ambiente, "cliente", "/");
            try
            {
                using var navegadorAdmin = AbrirChrome(chrome, ambiente, "admin", "/admin/login");
                try
                {
                    await AguardarComandosAsync(ambiente, cancelar.Token);
                }
                finally
                {
                    await EncerrarChromeAsync(navegadorAdmin);
                }
            }
            finally
            {
                await EncerrarChromeAsync(navegadorCliente);
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception erro)
        {
            IEnumerable<Exception> falhas = erro is AggregateException grupo ? grupo.Flatten().InnerExceptions : [erro];
            foreach (var falha in falhas)
            {
                // Só mensagens próprias de pré-requisito e limpeza podem conter detalhes.
                Console.Error.WriteLine(falha switch
                {
                    FalhaRecursoDemonstracao => falha.Message,
                    SqlServerTestCleanupException sql => $"Banco descartável pendente de verificação: {sql.Banco} em .\\SQLEXPRESS.",
                    _ => $"Demonstração interrompida ({falha.GetType().Name}). Confira Chrome, build Angular e .\\SQLEXPRESS.",
                });
            }
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelarConsole;
        }
    }

    internal static string CaminhoChrome() => Environment.GetEnvironmentVariable("XAQUIAMUNI_CHROME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");

    internal static string CaminhoAngular()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var angular = Path.Combine(dir.FullName, "client", "dist", "xaquiamuni", "browser");
            if (File.Exists(Path.Combine(angular, "index.html"))) return angular;
        }
        throw new InvalidOperationException("Build Angular ausente.");
    }

    private static Process AbrirChrome(string executavel, AmbienteDemonstracao ambiente, string perfil, string caminho)
    {
        var start = new ProcessStartInfo(executavel) { UseShellExecute = false };
        foreach (var arg in new[]
        {
            "--user-data-dir=" + Path.Combine(ambiente.Temporario, perfil),
            "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-background-networking",
            "--proxy-server=127.0.0.1:1", "--ignore-certificate-errors-spki-list=" + ambiente.Host.PinSpki,
            ambiente.Host.Origin + caminho,
        }) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Chrome indisponível.");
    }

    private static async Task EncerrarChromeAsync(Process chrome)
    {
        try
        {
            if (chrome.HasExited) return;
            chrome.Kill(entireProcessTree: true);
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await chrome.WaitForExitAsync(limite.Token);
        }
        catch (Exception) when (!chrome.HasExited)
        {
            throw new FalhaRecursoDemonstracao($"Chrome da demonstração pendente, PID {chrome.Id}.");
        }
    }

    private static async Task AguardarComandosAsync(AmbienteDemonstracao ambiente, CancellationToken token)
    {
        var copiou = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(100, token);
                    continue;
                }

                var tecla = Console.ReadKey(intercept: true).KeyChar;
                if (tecla is 'q' or 'Q') return;
                string? conteudo = tecla switch
                {
                    '1' => ambiente.Admin.Email,
                    '2' => ambiente.Admin.Senha,
                    '3' => ambiente.Cliente.Email,
                    '4' => ambiente.Cliente.Senha,
                    '5' => await ambiente.ProximaConfirmacaoAsync(token),
                    '6' => "",
                    _ => null,
                };
                if (conteudo is null)
                {
                    if (tecla == '5') Console.WriteLine("Nenhum e-mail de confirmação pendente.");
                    continue;
                }

                await CopiarAsync(conteudo);
                copiou = tecla != '6';
                Console.WriteLine(tecla == '6' ? "Área de transferência limpa." : "Copiado. Cole na janela correspondente.");
            }
        }
        finally
        {
            if (copiou) await CopiarAsync("");
        }
    }

    private static async Task CopiarAsync(string conteudo)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "clip.exe"))
        {
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true,
        };
        using var clip = Process.Start(start) ?? throw new InvalidOperationException("Área de transferência indisponível.");
        await clip.StandardInput.WriteAsync(conteudo);
        clip.StandardInput.Close();
        await clip.WaitForExitAsync();
        if (clip.ExitCode != 0) throw new InvalidOperationException("Não foi possível copiar.");
    }
}

internal sealed class AmbienteDemonstracao(
    SqlServerTestDatabase banco, HostE2ECompra host, string temporario, ContaE2E admin, ContaE2E cliente) : IAsyncDisposable
{
    private const string Prefixo = "xaquiamuni-demo-";
    internal HostE2ECompra Host { get; } = host;
    internal string Temporario { get; } = temporario;
    internal ContaE2E Admin { get; } = admin;
    internal ContaE2E Cliente { get; } = cliente;
    internal string NomeBanco => banco.NomeBanco;
    internal string ConexaoIsolada => banco.ConexaoIsolada;
    internal required JornadaCompraE2E Jornada { get; init; }
    internal required object ProdutoPrincipal { get; init; }

    internal static async Task<AmbienteDemonstracao> CriarAsync(string angular)
    {
        var temporario = Directory.CreateTempSubdirectory(Prefixo).FullName;
        SqlServerTestDatabase? banco = null;
        HostE2ECompra? host = null;
        try
        {
            banco = await SqlServerTestDatabase.CreateAsync();
            host = new HostE2ECompra(banco, angular, temporario);
            var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
            var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
            var dados = new JornadaCompraE2E(host, new RelogioJornadaCompra(), admin.Id, cliente.Email);
            var produtoPrincipal = await dados.SemearAsync("Sabonete demonstração", "Sabonetes demonstração");
            await new JornadaCompraE2E(host, new RelogioJornadaCompra(), admin.Id, cliente.Email)
                .SemearAsync("Vela demonstração", "Velas demonstração");

            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IOptions<EmailConfirmationEmailOptions>>().Value.PublicFrontendBaseUrl = host.Origin;
            var pix = ConfiguracaoPixManual.Criar(TipoChavePixManual.Email, "pix-demonstracao@example.invalid",
                "Xaquiamuni Demonstração", "SIMULAÇÃO PARA O TCC. Não faça transferências.");
            await scope.ServiceProvider.GetRequiredService<IPagamentoPixManual>().SalvarAsync(pix, CancellationToken.None);
            return new AmbienteDemonstracao(banco, host, temporario, admin, cliente)
            {
                Jornada = dados,
                ProdutoPrincipal = produtoPrincipal,
            };
        }
        catch (Exception inicio)
        {
            try { await LimparAsync(banco, host, temporario); }
            catch (Exception limpeza) { throw new AggregateException(inicio, limpeza); }
            throw;
        }
    }

    internal async Task<string?> ProximaConfirmacaoAsync(CancellationToken token)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var lote = await sp.GetRequiredService<IEmailOutboxStore>().ClaimDueAsync(
            EmailOutboxMessageType.EmailConfirmation, 1, TimeSpan.FromMinutes(1), token);
        if (lote.Count == 0) return null;
        await sp.GetRequiredService<EmailConfirmationOutboxProcessor>().ProcessAsync(lote.Single(), token);
        if (!sp.GetRequiredService<CaixaEmailE2E>().Mensagens.TryDequeue(out var mensagem))
            throw new InvalidOperationException("O e-mail não foi capturado localmente.");

        var link = WebUtility.HtmlDecode(Regex.Match(mensagem.Content, "href=\"([^\"]+)\"").Groups[1].Value);
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != Host.Origin
            || uri.AbsolutePath != "/confirm-email" || uri.Fragment.Length <= 1)
            throw new InvalidOperationException("Link de confirmação local inválido.");
        return uri.AbsoluteUri;
    }

    public ValueTask DisposeAsync() => new(LimparAsync(banco, Host, Temporario));

    private static async Task LimparAsync(SqlServerTestDatabase? banco, HostE2ECompra? host, string temporario)
    {
        var falhas = new List<Exception>();
        if (host is not null)
        {
            try { await host.DisposeAsync(); }
            catch { falhas.Add(new FalhaRecursoDemonstracao("Falha ao encerrar o host HTTPS da demonstração.")); }
        }
        if (banco is not null)
        {
            var nome = banco.NomeBanco;
            try { await banco.DisposeAsync(); }
            catch { falhas.Add(new FalhaRecursoDemonstracao($"Banco descartável pendente de verificação: {nome} em .\\SQLEXPRESS.")); }
        }

        var alvo = Path.GetFullPath(temporario);
        var raiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (string.Equals(Path.GetDirectoryName(alvo), raiz, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(alvo).StartsWith(Prefixo, StringComparison.Ordinal))
        {
            try { Directory.Delete(alvo, recursive: true); }
            catch { falhas.Add(new FalhaRecursoDemonstracao($"Diretório temporário pendente: {alvo}")); }
        }
        else
        {
            falhas.Add(new FalhaRecursoDemonstracao($"Diretório de limpeza recusado: {alvo}"));
        }
        if (falhas.Count > 0) throw new AggregateException(falhas);
    }
}

internal sealed class FalhaRecursoDemonstracao(string mensagem) : Exception(mensagem);
