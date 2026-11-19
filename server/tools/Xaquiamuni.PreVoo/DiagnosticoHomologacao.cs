using System.Data.Common;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Xaquiamuni.PreVoo;

public static class DiagnosticoHomologacao
{
    public static int Executar(IConfiguration config, string ambiente, string servidorEsperado, string bancoEsperado, TextWriter saida)
    {
        var bloqueios = 0;
        void Verificar(bool ok, string nome, string sucesso, string falha, string acao, bool aviso = false)
        {
            if (!ok && !aviso) bloqueios++;
            saida.WriteLine($"[{(ok ? "OK" : aviso ? "AVISO" : "BLOQUEIO")}] {nome} — {(ok ? sucesso : falha)} Ação: {acao}");
        }

        Verificar(ambiente is "Staging" or "Homologacao", "Ambiente", "Ambiente de homologação declarado.",
            "Ambiente ausente ou fora do recorte.", "Use Staging ou Homologacao.");

        var erroSql = VerificarSql(config.GetConnectionString("SqlServer"), servidorEsperado, bancoEsperado);
        Verificar(erroSql is null, "SQL Server", "O destino declarado coincide. Nenhuma conexão foi aberta.",
            erroSql ?? "", "Confira o alvo aprovado e ConnectionStrings__SqlServer antes de um ensaio separado.");

        var origem = config["EmailConfirmation:PublicFrontendBaseUrl"];
        var origemValida = Uri.TryCreate(origem, UriKind.Absolute, out var url) && url.Scheme == "https"
            && url.Host.Length > 0 && !url.IsLoopback && url.UserInfo.Length == 0
            && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Fragment.Length == 0;
        Verificar(origemValida, "Origem pública HTTPS", "Origem HTTPS declarada.", "Origem ausente ou inválida.",
            "Defina EmailConfirmation__PublicFrontendBaseUrl como origem HTTPS sem caminho ou credenciais.");

        var proxy = Habilitado(config, "PreVoo:Https:Proxy");
        var tls = proxy || config.GetSection("Kestrel:Endpoints").GetChildren().Any(e => UrlHttps(e["Url"]))
            || (config["urls"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(UrlHttps);
        Verificar(tls, "Terminação HTTPS", "Terminação local ou por proxy declarada.", "Terminação TLS não declarada.",
            "Configure HTTPS no Kestrel ou declare PreVoo__Https__Proxy=true e valide a configuração do proxy.");

        var imagens = config["ProductImages:Storage:RootPath"];
        Verificar(CaminhoDedicado(imagens), "Imagens", "Caminho dedicado declarado.", "Caminho ausente, inválido ou temporário.",
            "Defina ProductImages__Storage__RootPath e valide acesso, durabilidade e backup.");

        var keyRing = config["PreVoo:DataProtection:KeyRingPath"];
        var protecao = CaminhoDedicado(keyRing) && !Sobrepostos(keyRing!, imagens);
        Verificar(protecao, "Data Protection", "Caminho declarado apenas para o pré-voo.",
            "Falta um caminho durável separado das imagens.",
            "Declare PreVoo__DataProtection__KeyRingPath e configure persistência e proteção das chaves na implantação.");
        if (protecao)
            Verificar(false, "Data Protection na API", "", "A API não consome essa declaração.",
                "Valide persistência e rotação das chaves em homologação.", aviso: true);

        var versaoA = config["CpfProtection:CurrentEncryptionKeyVersion"];
        var versaoB = config["CpfProtection:CurrentFingerprintKeyVersion"];
        Verificar(ChavesCpfValidas(config, versaoA, versaoB), "Proteção de CPF", "Chaves ativas distintas declaradas.",
            "Faltam versões ativas com chaves independentes de 32 bytes em Base64.",
            "Configure CpfProtection por um provedor de segredos.");

        var token = config["ResendEmail:ApiKey"];
        var remetente = config["ResendEmail:FromEmail"];
        var email = Habilitado(config, "ResendEmail:Enabled") && Habilitado(config, "EmailOutboxWorker:Enabled")
            && token is not null && Regex.IsMatch(token, @"\A[A-Za-z0-9_.~+/\-]+=*\z")
            && MailAddress.TryCreate(remetente, out var endereco) && endereco.Address == remetente
            && endereco.Host.Contains('.') && !endereco.Host.StartsWith('.') && !endereco.Host.EndsWith('.')
            && !endereco.Host.Contains("..");
        Verificar(email, "E-mail e Outbox", "Remetente e processamento habilitados em configuração.",
            "Remetente, credencial ou worker ausente, desabilitado ou inválido.",
            "Configure ResendEmail e EmailOutboxWorker e valide a entrega separadamente.");

        var expiracao = bool.TryParse(config["ExpiracaoPedidosWorker:Enabled"], out var ativa);
        Verificar(expiracao, "Worker de expiração", ativa ? "Ativação explícita." : "Desativação explícita.",
            "Estado indefinido ou inválido.", "Defina ExpiracaoPedidosWorker__Enabled.");
        if (expiracao && !ativa)
            Verificar(false, "Vencimentos", "", "A expiração automática ficará desabilitada.",
                "Defina como processar pedidos vencidos no ensaio.", aviso: true);

        var logs = Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(config["Logging:LogLevel:Default"],
            ignoreCase: true, out var nivel) && Enum.IsDefined(nivel) && nivel != Microsoft.Extensions.Logging.LogLevel.None;
        var monitoramento = !string.IsNullOrWhiteSpace(config["PreVoo:Monitoramento:Destino"]);
        Verificar(logs && monitoramento, "Logs e monitoramento", "Nível e destino declarados, sem validar coleta.",
            "Estratégia de logs ou destino de monitoramento não declarado.",
            "Configure Logging__LogLevel__Default e PreVoo__Monitoramento__Destino. Valide alertas e redação.", aviso: true);

        Verificar(!new[] { versaoA, versaoB }.Any(v => string.Equals(v, "e2e", StringComparison.OrdinalIgnoreCase)),
            "Isolamento da demonstração", "Nenhum marcador de chave E2E.", "Foi detectada uma versão de chave E2E.",
            "Use segredos próprios de homologação e preserve o iniciador de demonstração separado.");

        saida.WriteLine(bloqueios == 0 ? "Pré-voo estático sem bloqueios." : $"Pré-voo estático com {bloqueios} bloqueio(s).");
        saida.WriteLine("Conectividade, TLS, envio de e-mail, backup e recuperação exigem ensaio separado.");
        return bloqueios == 0 ? 0 : 1;
    }

    private static string? VerificarSql(string? valor, string servidorEsperado, string bancoEsperado)
    {
        if (!DestinoValido(servidorEsperado, bancoEsperado))
            return "Alvo esperado ausente, ambíguo ou proibido.";
        if (string.IsNullOrWhiteSpace(valor))
            return "A conexão SQL não está configurada.";

        SqlConnectionStringBuilder sql;
        try
        {
            sql = new SqlConnectionStringBuilder(valor);
            var campos = new DbConnectionStringBuilder { ConnectionString = valor };
            var chaves = campos.Keys.Cast<string>().Select(chave => chave.ToLowerInvariant()).ToArray();
            if (chaves.Count(c => c is "server" or "data source" or "address" or "addr" or "network address") > 1
                || chaves.Count(c => c is "database" or "initial catalog") > 1)
                return "A conexão declara mais de um alias para servidor ou banco.";
        }
        catch (ArgumentException) { return "A conexão SQL não pôde ser interpretada."; }
        if (!DestinoValido(sql.DataSource, sql.InitialCatalog) || sql.UserInstance
            || !string.IsNullOrEmpty(sql.AttachDBFilename) || !string.IsNullOrEmpty(sql.FailoverPartner))
            return "Destino configurado proibido ou com redirecionamento não verificado.";

        if (!string.Equals(NormalizarServidor(sql.DataSource), NormalizarServidor(servidorEsperado), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sql.InitialCatalog, bancoEsperado, StringComparison.Ordinal))
            return "Servidor ou banco diverge do alvo esperado.";
        if (sql.Encrypt == SqlConnectionEncryptOption.Optional || sql.TrustServerCertificate)
            return "Exija Encrypt=Mandatory ou Strict e TrustServerCertificate=False.";
        return null;
    }

    private static string NormalizarServidor(string valor) =>
        valor.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? valor[4..] : valor;

    private static bool DestinoValido(string servidor, string banco) =>
        Regex.IsMatch(NormalizarServidor(servidor), @"\A(?:\.|[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*)(?:\\[A-Za-z0-9_-]+|,[1-9][0-9]{0,4})?\z")
        && Regex.IsMatch(banco, @"\A[A-Za-z0-9_-]{1,128}\z")
        && !Regex.IsMatch(banco, @"\A(?:master|model|msdb|tempdb|xaquiamuni)\z|(?:^|[_-])(?:dev(?:elopment)?|prod(?:uction|ucao)?|demo)(?:[_-]|$)|(?:dev(?:elopment)?|prod(?:uction|ucao)?)\z|integrationtest|demonstracao",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool UrlHttps(string? valor) =>
        Uri.TryCreate(valor, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0;

    private static bool CaminhoDedicado(string? valor) => !string.IsNullOrWhiteSpace(valor)
        && Path.IsPathFullyQualified(valor) && !Sobrepostos(Path.GetTempPath(), valor);

    private static bool Sobrepostos(string primeiro, string? segundo)
    {
        if (string.IsNullOrWhiteSpace(segundo) || !Path.IsPathFullyQualified(segundo)) return false;
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(primeiro)) + Path.DirectorySeparatorChar;
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(segundo)) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Habilitado(IConfiguration config, string campo) =>
        bool.TryParse(config[campo], out var habilitado) && habilitado;

    private static bool ChavesCpfValidas(IConfiguration config, string? versaoA, string? versaoB)
    {
        if (string.IsNullOrWhiteSpace(versaoA) || string.IsNullOrWhiteSpace(versaoB)) return false;
        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        try
        {
            return Convert.TryFromBase64String(config[$"CpfProtection:EncryptionKeys:{versaoA}"] ?? "", a, out var n) && n == 32
                && Convert.TryFromBase64String(config[$"CpfProtection:FingerprintKeys:{versaoB}"] ?? "", b, out var m) && m == 32
                && !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(a);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(b);
        }
    }
}
