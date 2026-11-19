using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Xaquiamuni.PreVoo;

namespace Xaquiamuni.Tests;

public sealed class PreVooHomologacaoTests
{
    private const string Servidor = "sql.homologacao.invalid";
    private const string Banco = "XaquiamuniHomologacao";
    private const string Senha = "senha-sintetica-NAO-EXIBIR";
    private const string Token = "token-sintetico-NAO-EXIBIR";
    private static string Conexao(string servidor, string banco) =>
        $"Server={servidor};Database={banco};User ID=conta_sintetica;Password={Senha};Encrypt=Mandatory;TrustServerCertificate=False";

    [Fact]
    public void ConfiguracaoSinteticaValidaTemDiagnosticoSemSegredos()
    {
        var (codigo, saida) = Executar(Configuracao());
        Assert.Equal(0, codigo);
        Assert.Contains("[OK] SQL Server", saida);
        Assert.Contains("[AVISO] Data Protection na API", saida);
        Assert.Contains("ensai", saida);
        VerificarRedacao(saida);
    }

    [Theory]
    [InlineData("ConnectionStrings:SqlServer", null, "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=host", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=host;Database=XaquiamuniDev", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=host;Database=Xaquiamuni_IntegrationTest_123", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=host;Database=XaquiamuniProd", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=host;Database=Outro", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=host;Data Source=sql.homologacao.invalid;Database=XaquiamuniHomologacao", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "Server=sql.homologacao.invalid;Database=XaquiamuniHomologacao;Encrypt=False", "SQL Server")]
    [InlineData("ConnectionStrings:SqlServer", "KeywordInválida=token-sintetico-NAO-EXIBIR", "SQL Server")]
    [InlineData("EmailConfirmation:PublicFrontendBaseUrl", "http://loja.invalid", "Origem pública HTTPS")]
    [InlineData("PreVoo:Https:Proxy", null, "Terminação HTTPS")]
    [InlineData("ResendEmail:ApiKey", null, "E-mail e Outbox")]
    [InlineData("EmailOutboxWorker:Enabled", "false", "E-mail e Outbox")]
    [InlineData("PreVoo:DataProtection:KeyRingPath", null, "Data Protection")]
    [InlineData("ProductImages:Storage:RootPath", null, "Imagens")]
    [InlineData("ExpiracaoPedidosWorker:Enabled", null, "Worker de expiração")]
    [InlineData("CpfProtection:CurrentEncryptionKeyVersion", "e2e", "Isolamento da demonstração")]
    public void ConfiguracaoIncompletaOuIncompativelBloqueia(string campo, string? valor, string verificacao)
    {
        var config = Configuracao();
        config[campo] = valor;
        var (codigo, saida) = Executar(config);
        Assert.Equal(1, codigo);
        Assert.Contains($"[BLOQUEIO] {verificacao} —", saida);
        VerificarRedacao(saida);
    }

    [Theory]
    [InlineData("", Banco)]
    [InlineData(Servidor, "")]
    [InlineData(Servidor, "XaquiamuniDev")]
    [InlineData(Servidor, "master")]
    public void AlvoEsperadoAusenteOuProibidoBloqueia(string servidor, string banco)
    {
        var (codigo, saida) = Executar(Configuracao(), servidor, banco);
        Assert.Equal(1, codigo);
        Assert.Contains("Alvo esperado ausente, ambíguo ou proibido", saida);
    }

    [Fact]
    public void DestinoCompativelNaoAbreConexaoNemEmLoopback()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endereco = "127.0.0.1," + ((IPEndPoint)listener.LocalEndpoint).Port;
        var config = Configuracao();
        config["ConnectionStrings:SqlServer"] = Conexao("tcp:" + endereco, Banco);
        var (codigo, _) = Executar(config, endereco, Banco);
        Assert.Equal(0, codigo);
        Assert.False(listener.Pending());
    }

    private static (int Codigo, string Saida) Executar(IConfiguration config, string servidor = Servidor, string banco = Banco)
    {
        using var saida = new StringWriter();
        var codigo = DiagnosticoHomologacao.Executar(config, "Staging", servidor, banco, saida);
        return (codigo, saida.ToString());
    }

    private static void VerificarRedacao(string saida)
    {
        Assert.DoesNotContain(Senha, saida);
        Assert.DoesNotContain(Token, saida);
        Assert.DoesNotContain("conta_sintetica", saida);
        Assert.DoesNotContain(Servidor, saida);
        Assert.DoesNotContain("sender@example.invalid", saida);
        Assert.DoesNotContain(Chave(1), saida);
    }

    private static string Chave(byte valor) => Convert.ToBase64String(Enumerable.Repeat(valor, 32).ToArray());

    private static IConfigurationRoot Configuracao()
    {
        var raiz = Path.GetPathRoot(AppContext.BaseDirectory)!;
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = Conexao(Servidor, Banco),
            ["EmailConfirmation:PublicFrontendBaseUrl"] = "https://loja.homologacao.invalid",
            ["PreVoo:Https:Proxy"] = "true",
            ["ProductImages:Storage:RootPath"] = Path.Combine(raiz, "xaquiamuni-homologacao", "imagens"),
            ["PreVoo:DataProtection:KeyRingPath"] = Path.Combine(raiz, "xaquiamuni-homologacao", "chaves"),
            ["PreVoo:Monitoramento:Destino"] = "coletor-sintetico",
            ["CpfProtection:CurrentEncryptionKeyVersion"] = "homol",
            ["CpfProtection:EncryptionKeys:homol"] = Chave(1),
            ["CpfProtection:CurrentFingerprintKeyVersion"] = "homol",
            ["CpfProtection:FingerprintKeys:homol"] = Chave(2),
            ["ResendEmail:Enabled"] = "true",
            ["ResendEmail:ApiKey"] = Token,
            ["ResendEmail:FromEmail"] = "sender@example.invalid",
            ["EmailOutboxWorker:Enabled"] = "true",
            ["ExpiracaoPedidosWorker:Enabled"] = "true",
            ["Logging:LogLevel:Default"] = "Information",
        }).Build();
    }
}
