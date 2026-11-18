using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Xaquiamuni.Dominio.Entidades;

public enum TipoChavePixManual { Aleatoria = 1, Email = 2, Telefone = 3 }

public sealed class ConfiguracaoPixManual
{
    public const int ChaveMaxLength = 77;
    public const int NomeMaxLength = 150;
    public const int InstrucoesMaxLength = 500;

    private ConfiguracaoPixManual() { }

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
        _ => false,
    };
}
