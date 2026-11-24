using System.ComponentModel.DataAnnotations;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class Fornecedor
{
    public const int NomeMax = 120;
    public const int TelefoneMax = 30;
    public const int EmailMax = 254;
    public const int ObservacoesMax = 1000;

    private Fornecedor() { }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = null!;
    public string NomeNormalizado { get; private set; } = null!;
    public string? Telefone { get; private set; }
    public string? Email { get; private set; }
    public string? Observacoes { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset AtualizadoEmUtc { get; private set; }
    public Guid Versao { get; private set; }
    public bool? UtilizadoEmCompra { get; private set; }

    public static Fornecedor Criar(string? nome, string? telefone, string? email, string? observacoes,
        DateTimeOffset agora)
    {
        var fornecedor = new Fornecedor { Id = Guid.NewGuid(), Ativo = true, CriadoEmUtc = agora.ToUniversalTime(),
            UtilizadoEmCompra = false };
        fornecedor.Editar(nome, telefone, email, observacoes, agora);
        return fornecedor;
    }

    public void Editar(string? nome, string? telefone, string? email, string? observacoes, DateTimeOffset agora)
    {
        var nomeLimpo = NormalizarNome(nome);
        var telefoneLimpo = Opcional(telefone, TelefoneMax, "O telefone");
        var emailLimpo = Opcional(email, EmailMax, "O e-mail");
        var observacoesLimpas = Opcional(observacoes, ObservacoesMax, "O campo de observações");
        if (nomeLimpo.Length is 0 or > NomeMax)
            throw new ArgumentException($"Informe um nome com até {NomeMax} caracteres.");
        if (telefoneLimpo is not null &&
            (telefoneLimpo.Any(c => !(char.IsAsciiDigit(c) || " +()-.".Contains(c))) ||
                telefoneLimpo.Count(char.IsAsciiDigit) is < 8 or > 15))
            throw new ArgumentException("Informe um telefone com 8 a 15 dígitos.");
        if (emailLimpo is not null && (emailLimpo.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(emailLimpo)))
            throw new ArgumentException("Informe um e-mail válido.");

        Nome = nomeLimpo;
        NomeNormalizado = nomeLimpo.ToUpperInvariant();
        Telefone = telefoneLimpo;
        Email = emailLimpo;
        Observacoes = observacoesLimpas;
        Versao = Guid.NewGuid();
        AtualizadoEmUtc = agora.ToUniversalTime();
    }

    public void DefinirAtivo(bool ativo, DateTimeOffset agora)
    {
        if (Ativo == ativo) return;
        Ativo = ativo;
        Versao = Guid.NewGuid();
        AtualizadoEmUtc = agora.ToUniversalTime();
    }

    public static string NormalizarNome(string? nome) =>
        string.Join(' ', (nome ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Opcional(string? valor, int limite, string campo)
    {
        var limpo = valor?.Trim();
        if (string.IsNullOrEmpty(limpo)) return null;
        if (limpo.Length > limite)
            throw new ArgumentException($"{campo} deve ter até {limite} caracteres.");
        return limpo;
    }
}
