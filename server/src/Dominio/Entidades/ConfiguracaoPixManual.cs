using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Xaquiamuni.Dominio.Entidades;

public enum TipoChavePixManual { Aleatoria = 1, Email = 2, Telefone = 3, Cnpj = 4 }

public sealed class ConfiguracaoPixManual
{
    public const int ChaveMaxLength = 77;
    public const int NomeMaxLength = 150;
    public const int InstrucoesMaxLength = 500;

    private ConfiguracaoPixManual() { }

    public long Revisao { get; private set; } = 1;

    public void DefinirRevisao(long revisao) => Revisao = revisao;

    public int Id { get; private set; } = 1;
    public TipoChavePixManual TipoChave { get; private set; }
    public string ChavePix { get; private set; } = null!;
    public string NomeRecebedor { get; private set; } = null!;
    public string? InstrucoesAdicionais { get; private set; }

    public static ConfiguracaoPixManual Criar(TipoChavePixManual tipoChave, string? chavePix,
        string? nomeRecebedor, string? instrucoesAdicionais)
    {
        if (!Enum.IsDefined(tipoChave)) throw new ArgumentException("Selecione um tipo de chave aceito.", nameof(tipoChave));
        var chave = chavePix?.Trim();
        if (tipoChave == TipoChavePixManual.Cnpj && chave is not null)
            chave = NormalizarCnpj(chave);
        if (chave is not { Length: > 0 and <= ChaveMaxLength } || !ChaveValida(tipoChave, chave))
            throw new ArgumentException("Informe uma chave no formato do tipo selecionado.", nameof(chavePix));
        var nome = nomeRecebedor?.Trim();
        if (nome is not { Length: > 0 and <= NomeMaxLength } || nome.Any(char.IsControl))
            throw new ArgumentException("Informe o nome do recebedor, com até 150 caracteres.", nameof(nomeRecebedor));
        var instrucoes = instrucoesAdicionais?.Trim();
        if (instrucoes?.Length > InstrucoesMaxLength || instrucoes?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true)
            throw new ArgumentException("As instruções devem ter até 500 caracteres.", nameof(instrucoesAdicionais));

        return new ConfiguracaoPixManual
        {
            TipoChave = tipoChave,
            ChavePix = tipoChave is TipoChavePixManual.Email or TipoChavePixManual.Aleatoria ? chave.ToLowerInvariant() : chave,
            NomeRecebedor = nome,
            InstrucoesAdicionais = string.IsNullOrEmpty(instrucoes) ? null : instrucoes,
        };
    }

    private static bool ChaveValida(TipoChavePixManual tipo, string chave) => tipo switch
    {
        TipoChavePixManual.Aleatoria => chave.Length == 36 && Guid.TryParseExact(chave, "D", out var id) && id != Guid.Empty,
        TipoChavePixManual.Email => chave.All(c => c is >= '!' and <= '~')
            && MailAddress.TryCreate(chave, out var email) && email.Address == chave && email.Host.Contains('.'),
        TipoChavePixManual.Telefone => Regex.IsMatch(chave, @"\A\+[1-9][0-9]{1,14}\z"),
        TipoChavePixManual.Cnpj => true, // O formato e os dígitos já foram validados na normalização.
        _ => false,
    };

    private static string? NormalizarCnpj(string chave)
    {
        if (!Regex.IsMatch(chave, @"\A(?:[A-Za-z0-9]{12}[0-9]{2}|[A-Za-z0-9]{2}\.[A-Za-z0-9]{3}\.[A-Za-z0-9]{3}/[A-Za-z0-9]{4}-[0-9]{2})\z"))
            return null;

        var cnpj = chave.Replace(".", "").Replace("/", "").Replace("-", "").ToUpperInvariant();
        if (cnpj == "00000000000000"
            || cnpj[12] - '0' != CalcularDigito(cnpj.AsSpan(0, 12))
            || cnpj[13] - '0' != CalcularDigito(cnpj.AsSpan(0, 13)))
            return null;

        return cnpj;
    }

    private static int CalcularDigito(ReadOnlySpan<char> baseCnpj)
    {
        var soma = 0;
        for (var i = 0; i < baseCnpj.Length; i++)
            soma += (baseCnpj[^(i + 1)] - '0') * (2 + i % 8);

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
