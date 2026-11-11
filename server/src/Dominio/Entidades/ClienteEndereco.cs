using System.Text;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class ClienteEndereco
{
    private const int CepLength = 8;
    private const int UfLength = 2;
    private const int CidadeMaxLength = 100;
    private const int BairroMaxLength = 100;
    private const int LogradouroMaxLength = 150;
    private const int NumeroMaxLength = 20;
    private const int ComplementoMaxLength = 100;

    private static readonly HashSet<string> UfsBrasileiras = new(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS",
        "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC",
        "SP", "SE", "TO"
    };

    private ClienteEndereco(
        Guid id,
        Guid clienteUserId,
        string cep,
        string uf,
        string cidade,
        string bairro,
        string logradouro,
        string numero,
        string? complemento,
        bool isDefault)
    {
        Id = id;
        ClienteUserId = clienteUserId;
        Cep = cep;
        Uf = uf;
        Cidade = cidade;
        Bairro = bairro;
        Logradouro = logradouro;
        Numero = numero;
        Complemento = complemento;
        IsDefault = isDefault;
    }

    public Guid Id { get; }
    public Guid ClienteUserId { get; }
    public string Cep { get; private set; }
    public string Uf { get; private set; }
    public string Cidade { get; private set; }
    public string Bairro { get; private set; }
    public string Logradouro { get; private set; }
    public string Numero { get; private set; }
    public string? Complemento { get; private set; }
    public bool IsDefault { get; private set; }

    public static ClienteEndereco Criar(
        Guid id,
        Guid clienteUserId,
        string? cep,
        string? uf,
        string? cidade,
        string? bairro,
        string? logradouro,
        string? numero,
        string? complemento,
        bool isDefault)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador do endereço é obrigatório.", nameof(id));
        }

        if (clienteUserId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do cliente é obrigatório.", nameof(clienteUserId));
        }

        return new ClienteEndereco(
            id,
            clienteUserId,
            NormalizarCep(cep),
            NormalizarUf(uf),
            NormalizarObrigatorio(cidade, CidadeMaxLength, "A cidade", nameof(cidade)),
            NormalizarObrigatorio(bairro, BairroMaxLength, "O bairro", nameof(bairro)),
            NormalizarObrigatorio(logradouro, LogradouroMaxLength, "O logradouro", nameof(logradouro)),
            NormalizarObrigatorio(numero, NumeroMaxLength, "O número", nameof(numero)),
            NormalizarOpcional(complemento, ComplementoMaxLength, "O complemento", nameof(complemento)),
            isDefault);
    }

    public void AtualizarDados(
        string? cep,
        string? uf,
        string? cidade,
        string? bairro,
        string? logradouro,
        string? numero,
        string? complemento)
    {
        var normalizedCep = NormalizarCep(cep);
        var normalizedUf = NormalizarUf(uf);
        var normalizedCidade = NormalizarObrigatorio(cidade, CidadeMaxLength, "A cidade", nameof(cidade));
        var normalizedBairro = NormalizarObrigatorio(bairro, BairroMaxLength, "O bairro", nameof(bairro));
        var normalizedLogradouro = NormalizarObrigatorio(logradouro, LogradouroMaxLength, "O logradouro", nameof(logradouro));
        var normalizedNumero = NormalizarObrigatorio(numero, NumeroMaxLength, "O número", nameof(numero));
        var normalizedComplemento = NormalizarOpcional(complemento, ComplementoMaxLength, "O complemento", nameof(complemento));

        Cep = normalizedCep;
        Uf = normalizedUf;
        Cidade = normalizedCidade;
        Bairro = normalizedBairro;
        Logradouro = normalizedLogradouro;
        Numero = normalizedNumero;
        Complemento = normalizedComplemento;
    }

    internal void DefinirComoPadrao(bool isDefault)
    {
        IsDefault = isDefault;
    }

    public override string ToString() => "[ClienteEndereco]";

    private static string NormalizarCep(string? cep)
    {
        if (cep is null)
        {
            throw new ArgumentException("O CEP é obrigatório.", nameof(cep));
        }

        var normalized = cep.Trim();
        if (normalized.Length == 9 && normalized[5] == '-')
        {
            normalized = string.Concat(normalized.AsSpan(0, 5), normalized.AsSpan(6));
        }

        if (normalized.Length != CepLength || normalized.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException("O CEP deve conter oito dígitos.", nameof(cep));
        }

        return normalized;
    }

    private static string NormalizarUf(string? uf)
    {
        var normalized = uf?.Trim().ToUpperInvariant();
        if (normalized is null || !UfsBrasileiras.Contains(normalized))
        {
            throw new ArgumentException("A UF deve ser uma sigla brasileira válida.", nameof(uf));
        }

        return normalized;
    }

    private static string NormalizarObrigatorio(
        string? value,
        int maxLength,
        string fieldName,
        string parameterName)
    {
        var normalized = NormalizarEspacos(value);
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{fieldName} é obrigatório(a).", parameterName);
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} excede o limite permitido.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizarOpcional(
        string? value,
        int maxLength,
        string fieldName,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizarEspacos(value);
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} excede o limite permitido.", parameterName);
        }

        return normalized;
    }

    private static string NormalizarEspacos(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(character);
        }

        return normalized.ToString();
    }
}
