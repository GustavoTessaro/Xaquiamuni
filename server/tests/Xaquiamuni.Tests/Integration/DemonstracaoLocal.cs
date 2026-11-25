using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Outbox;

namespace Xaquiamuni.Tests.Integration;

public static class DemonstracaoLocal
{
    public static async Task<int> ExecutarAsync(string[] args)
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
            if (args.Length != 6 || !Guid.TryParseExact(args[1], "N", out _) ||
                !Path.IsPathFullyQualified(args[0]) || !File.Exists(Path.Combine(args[0], "index.html")))
                throw new FalhaRecursoDemonstracao("Inicie pelo script Iniciar-Demonstracao.ps1.");
            var chrome = CaminhoChrome();
            if (!File.Exists(chrome))
                throw new InvalidOperationException("Chrome não encontrado. Ajuste XAQUIAMUNI_CHROME.");
            var angular = args[0];
            var inventario = await AmbienteDemonstracao.InventarioAsync();
            Console.WriteLine($"SQL inicial ({inventario.Length}): {string.Join(", ", inventario)}");
            await using var ambiente = await AmbienteDemonstracao.CriarAsync(angular, inventario);
            await VerificarDisponibilidadeAsync(ambiente.Host, angular, cancelar.Token);
            cancelar.Token.ThrowIfCancellationRequested();
            Console.WriteLine($"Sessão {args[1]} · início {args[2]}");
            Console.WriteLine($"Git: {args[3]} · {args[4]} · alterações locais: {args[5]}");
            Console.WriteLine($"API: {ambiente.Host.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName} · módulo {typeof(Program).Module.ModuleVersionId}");
            Console.WriteLine($"Angular: {angular} · index SHA-256 {Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(angular, "index.html"), cancelar.Token)))}");
            Console.WriteLine($"Vitrine: {ambiente.Host.Origin}/");
            Console.WriteLine($"ADMIN: {ambiente.Host.Origin}/admin/login");
            Console.WriteLine($"SQL Server isolado: {ambiente.NomeBanco} · imagens: {Path.Combine(ambiente.Temporario, "imagens")}");
            Console.WriteLine($"CEP simulado aceito: {AmbienteDemonstracao.Cep} · Vacaria/RS · Rua da Demonstração, 100, Bairro de Teste.");
            Console.WriteLine("Retirada, entrega própria fictícia e cotação postal manual estão habilitadas. Há cadastros para testar exclusões permitidas e bloqueadas.");
            Console.WriteLine("PIX fictício com confirmação manual. E-mails em memória. Workers de e-mail e expiração desabilitados. Nenhum envio, pagamento ou postagem real.");
            Console.WriteLine("Copiar para a área de transferência: 1 e-mail ADMIN, 2 senha ADMIN,");
            Console.WriteLine("3 e-mail CLIENTE, 4 senha CLIENTE, 5 confirmação local, 6 limpar.");
            Console.WriteLine("7 copiar o próximo link local de recuperação de senha.");
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
                    InventarioDemonstracaoDivergente => falha.Message,
                    SqlServerTestCleanupException sql => $"Banco descartável pendente de verificação: {sql.Banco} em .\\SQLEXPRESS.",
                    _ => $"Demonstração interrompida ({falha.GetType().Name}). Confira Chrome, build Angular e .\\SQLEXPRESS.",
                });
            }
            return falhas.Any(falha => falha is InventarioDemonstracaoDivergente) ? 2 : 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelarConsole;
        }
    }

    internal static async Task VerificarDisponibilidadeAsync(HostE2ECompra host, string angular, CancellationToken token)
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        handler.ServerCertificateCustomValidationCallback = (_, certificado, _, _) =>
        {
            using var chave = certificado?.GetRSAPublicKey();
            return chave is not null &&
                Convert.ToBase64String(SHA256.HashData(chave.ExportSubjectPublicKeyInfo())) == host.PinSpki;
        };
        using var cliente = new HttpClient(handler) { BaseAddress = new Uri(host.Origin), Timeout = TimeSpan.FromSeconds(10) };
        using var health = await cliente.GetAsync("/health", token);
        health.EnsureSuccessStatusCode();
        var servido = await cliente.GetByteArrayAsync("/", token);
        var esperado = await File.ReadAllBytesAsync(Path.Combine(angular, "index.html"), token);
        if (!servido.AsSpan().SequenceEqual(esperado))
            throw new FalhaRecursoDemonstracao("O HTML servido difere do build desta sessão.");
        var arquivos = Regex.Matches(Encoding.UTF8.GetString(esperado),
                @"(?:src|href)=""([A-Za-z0-9_.-]+\.(?:js|css))""")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        if (!arquivos.Any(arq => arq.EndsWith(".js", StringComparison.Ordinal)) ||
            !arquivos.Any(arq => arq.EndsWith(".css", StringComparison.Ordinal)))
            throw new FalhaRecursoDemonstracao("O build Angular não contém referências ao JavaScript e CSS.");
        foreach (var arquivo in arquivos)
        {
            var local = new FileInfo(Path.Combine(angular, arquivo));
            if (!local.Exists || local.Length == 0)
                throw new FalhaRecursoDemonstracao("O build Angular está incompleto.");
            using var cabecalho = new HttpRequestMessage(HttpMethod.Head, "/" + arquivo);
            using var resposta = await cliente.SendAsync(cabecalho, token);
            if (!resposta.IsSuccessStatusCode || resposta.Content.Headers.ContentLength != local.Length)
                throw new FalhaRecursoDemonstracao("Um recurso do build Angular não pôde ser servido.");
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
                    '7' => await ambiente.ProximaRecuperacaoAsync(token),
                    _ => null,
                };
                if (conteudo is null)
                {
                    if (tecla == '5') Console.WriteLine("Nenhum e-mail de confirmação pendente.");
                    if (tecla == '7') Console.WriteLine("Nenhum e-mail de recuperação pendente.");
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
    SqlServerTestDatabase banco, HostE2ECompra host, string temporario, ContaE2E admin, ContaE2E cliente,
    string[]? inventarioInicial) : IAsyncDisposable
{
    private const string Prefixo = "xaquiamuni-demo-";
    internal const string Cep = "95200000";
    private const string Ibge = "4322509";
    internal HostE2ECompra Host { get; } = host;
    internal string Temporario { get; } = temporario;
    internal ContaE2E Admin { get; } = admin;
    internal ContaE2E Cliente { get; } = cliente;
    internal string NomeBanco => banco.NomeBanco;
    internal string ConexaoIsolada => banco.ConexaoIsolada;
    internal required JornadaCompraE2E Jornada { get; init; }
    internal required object ProdutoPrincipal { get; init; }

    internal static async Task<AmbienteDemonstracao> CriarAsync(string angular, string[]? inventarioInicial = null)
    {
        var temporario = Directory.CreateTempSubdirectory(Prefixo).FullName;
        SqlServerTestDatabase? banco = null;
        HostE2ECompra? host = null;
        try
        {
            banco = await SqlServerTestDatabase.CreateAsync();
            // Este host só aceita o banco descartável que acabou de ser criado.
            host = new HostE2ECompra(banco, angular, temporario, promocoesComerciais: true,
                municipiosPorCep: new Dictionary<string, string> { [Cep] = Ibge },
                municipiosPostais: new Dictionary<string, MunicipioPostal> { [Cep] = new(Ibge, "Vacaria", "RS") });
            var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
            var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
            var dados = new JornadaCompraE2E(host, new RelogioJornadaCompra(), admin.Id, cliente.Email);
            var produtoPrincipal = await dados.SemearAsync("Sabonete demonstração", "Sabonetes demonstração");
            await new JornadaCompraE2E(host, new RelogioJornadaCompra(), admin.Id, cliente.Email)
                .SemearAsync("Vela demonstração", "Velas demonstração", comVariante: true);

            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<XaquiamuniDbContext>();
            db.ClienteEnderecos.Add(ClienteEndereco.Criar(Guid.NewGuid(), cliente.Id, Cep, "RS", "Vacaria",
                "Bairro de Teste", "Rua da Demonstração", "100", null, true));
            db.Produtos.Add(Produto.CriarSimples("Produto livre para exclusão", null,
                Categoria.Criar("Cadastros de teste", null, 0), "UN", 10m));
            db.Categorias.Add(Categoria.Criar("Categoria vazia para exclusão", null, 0));
            await db.SaveChangesAsync();
            var atendimento = scope.ServiceProvider.GetRequiredService<IConfiguracoesAtendimento>();
            var token = CancellationToken.None;
            ExigirSucesso(await atendimento.SalvarCidadeAsync(null, new(Ibge, "Vacaria", "RS", 1250, true),
                admin.Id, Guid.NewGuid(), token));
            ExigirSucesso(await atendimento.AtualizarModalidadesAsync(
                new(true, true, false, "Balcão fictício da demonstração", "Apresente o número do pedido.", 0),
                admin.Id, Guid.NewGuid(), token));
            var configuracao = await atendimento.ConsultarAsync(token);
            ExigirSucesso(await atendimento.AtualizarCorreiosManualAsync(new(true, 48, configuracao.Revisao),
                admin.Id, Guid.NewGuid(), token));
            scope.ServiceProvider.GetRequiredService<IOptions<EmailConfirmationEmailOptions>>().Value.PublicFrontendBaseUrl = host.Origin;
            var pix = ConfiguracaoPixManual.Criar(TipoChavePixManual.Email, "pix-demonstracao@example.invalid",
                "Xaquiamuni Demonstração", "SIMULAÇÃO PARA O TCC. Não faça transferências.");
            await scope.ServiceProvider.GetRequiredService<IPagamentoPixManual>().SalvarAsync(pix, CancellationToken.None);
            return new AmbienteDemonstracao(banco, host, temporario, admin, cliente, inventarioInicial)
            {
                Jornada = dados,
                ProdutoPrincipal = produtoPrincipal,
            };
        }
        catch (Exception inicio)
        {
            try { await LimparAsync(banco, host, temporario, inventarioInicial); }
            catch (Exception limpeza) { throw new AggregateException(inicio, limpeza); }
            throw;
        }
    }

    private static void ExigirSucesso(ResultadoAtendimento resultado)
    {
        if (resultado.Status != StatusAtendimento.Sucesso)
            throw new FalhaRecursoDemonstracao("Não foi possível configurar as modalidades sintéticas.");
    }

    internal Task<string?> ProximaConfirmacaoAsync(CancellationToken token) =>
        ProximoLinkAsync(EmailOutboxMessageType.EmailConfirmation, "/confirm-email", token);

    internal Task<string?> ProximaRecuperacaoAsync(CancellationToken token) =>
        ProximoLinkAsync(EmailOutboxMessageType.PasswordReset, "/reset-password", token);

    private async Task<string?> ProximoLinkAsync(EmailOutboxMessageType tipo, string caminho, CancellationToken token)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var lote = await sp.GetRequiredService<IEmailOutboxStore>().ClaimDueAsync(
            tipo, 1, TimeSpan.FromMinutes(1), token);
        if (lote.Count == 0) return null;
        IEmailOutboxProcessor processador = tipo == EmailOutboxMessageType.EmailConfirmation
            ? sp.GetRequiredService<EmailConfirmationOutboxProcessor>()
            : sp.GetRequiredService<PasswordResetOutboxProcessor>();
        await processador.ProcessAsync(lote.Single(), token);
        if (!sp.GetRequiredService<CaixaEmailE2E>().Mensagens.TryDequeue(out var mensagem))
            return null;

        var link = WebUtility.HtmlDecode(Regex.Match(mensagem.Content, "href=\"([^\"]+)\"").Groups[1].Value);
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != Host.Origin
            || uri.AbsolutePath != caminho || uri.Fragment.Length <= 1)
            throw new InvalidOperationException("Link de e-mail local inválido.");
        return uri.AbsoluteUri;
    }

    public ValueTask DisposeAsync() => new(LimparAsync(banco, Host, Temporario, inventarioInicial));

    internal static async Task<string[]> InventarioAsync()
    {
        await using var conexao = new SqlConnection(@"Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5");
        await conexao.OpenAsync();
        await SqlServerTestDatabase.ValidarInstanciaMasterAsync(conexao);
        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT name FROM sys.databases ORDER BY name";
        await using var resultado = await comando.ExecuteReaderAsync();
        var nomes = new List<string>();
        while (await resultado.ReadAsync()) nomes.Add(resultado.GetString(0));
        return nomes.ToArray();
    }

    private static async Task VerificarInventarioAsync(string[] esperado, string fase)
    {
        var atual = await InventarioAsync();
        Console.WriteLine($"SQL {fase} ({atual.Length}): {string.Join(", ", atual)}");
        var faltantes = esperado.Except(atual).ToArray();
        var novos = atual.Except(esperado).ToArray();
        if (faltantes.Length + novos.Length > 0)
            throw new InventarioDemonstracaoDivergente($"Inventário SQL divergente. Ausentes: [{string.Join(", ", faltantes)}]. Novos: [{string.Join(", ", novos)}]. Limpeza adicional interrompida.");
    }

    private static async Task LimparAsync(SqlServerTestDatabase? banco, HostE2ECompra? host, string temporario, string[]? inventario)
    {
        var falhas = new List<Exception>();
        if (host is not null)
        {
            try { await host.DisposeAsync(); }
            catch { falhas.Add(new FalhaRecursoDemonstracao("Falha ao encerrar o host HTTPS da demonstração.")); }
        }
        if (inventario is not null)
            await VerificarInventarioAsync(banco is null ? inventario : [.. inventario, banco.NomeBanco], "antes do descarte");
        if (banco is not null)
        {
            var nome = banco.NomeBanco;
            try { await banco.DisposeAsync(); Console.WriteLine($"Banco da sessão descartado: {nome}"); }
            catch { falhas.Add(new FalhaRecursoDemonstracao($"Banco descartável pendente de verificação: {nome} em .\\SQLEXPRESS.")); }
        }
        if (inventario is not null) await VerificarInventarioAsync(inventario, "final");

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
internal sealed class InventarioDemonstracaoDivergente(string mensagem) : Exception(mensagem);
