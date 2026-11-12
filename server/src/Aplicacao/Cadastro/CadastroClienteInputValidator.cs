using System.Net.Mail;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Aplicacao.Cadastro;

public sealed class CadastroClienteInputValidator
{
    private const int NomeCompletoMaxLength = 150;
    private const int EmailMaxLength = 256;
    private const int SenhaMaxLength = 256;

    public CadastroClienteInputValidationResult Validate(CadastroClienteInput? input)
    {
        var erros = new List<CadastroClienteInputValidationError>();
        if (input is null)
        {
            AddError(erros, "Cadastro", "Os dados do cadastro são obrigatórios.");
            return Result(erros);
        }

        ValidateNome(input.NomeCompleto, erros);

        if (string.IsNullOrWhiteSpace(input.Cpf))
        {
            AddError(erros, nameof(input.Cpf), "O CPF é obrigatório.");
        }
        else if (!Cpf.TryCreate(input.Cpf, out _))
        {
            AddError(erros, nameof(input.Cpf), "O CPF informado é inválido.");
        }

        ValidateEmail(input.Email, erros);
        ValidateTelefone(input.Telefone, erros);
        ValidateSenha(input.Senha, erros);
        ValidateEndereco(input.EnderecoInicial, erros);

        return Result(erros);
    }

    public CadastroClienteInputValidationResult ValidateEmail(string? email)
    {
        var erros = new List<CadastroClienteInputValidationError>();
        ValidateEmail(email, erros);
        return Result(erros);
    }

    private static void ValidateNome(string? nomeCompleto, List<CadastroClienteInputValidationError> erros)
    {
        if (string.IsNullOrWhiteSpace(nomeCompleto))
        {
            AddError(erros, nameof(CadastroClienteInput.NomeCompleto), "O nome completo é obrigatório.");
            return;
        }

        var normalized = string.Join(' ', nomeCompleto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > NomeCompletoMaxLength)
        {
            AddError(erros, nameof(CadastroClienteInput.NomeCompleto), "O nome completo excede 150 caracteres.");
        }
    }

    private static void ValidateEmail(string? email, List<CadastroClienteInputValidationError> erros)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            AddError(erros, nameof(CadastroClienteInput.Email), "O e-mail é obrigatório.");
            return;
        }

        var value = email.Trim();
        if (value.Length > EmailMaxLength
            || !MailAddress.TryCreate(value, out var address)
            || !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase)
            || !HasDottedDomain(value))
        {
            AddError(erros, nameof(CadastroClienteInput.Email), "O e-mail informado é inválido.");
        }
    }

    private static bool HasDottedDomain(string email)
    {
        var separator = email.LastIndexOf('@');
        if (separator <= 0 || separator == email.Length - 1)
        {
            return false;
        }

        var domain = email.AsSpan(separator + 1);
        return domain.Contains('.')
            && domain[0] != '.'
            && domain[^1] != '.'
            && !domain.Contains("..", StringComparison.Ordinal);
    }

    private static void ValidateTelefone(string? telefone, List<CadastroClienteInputValidationError> erros)
    {
        if (string.IsNullOrWhiteSpace(telefone))
        {
            AddError(erros, nameof(CadastroClienteInput.Telefone), "O telefone é obrigatório.");
            return;
        }

        if (!TryNormalizeTelefone(telefone, out var digits) || !IsBrazilianPhone(digits))
        {
            AddError(erros, nameof(CadastroClienteInput.Telefone), "O telefone brasileiro informado é inválido.");
        }
    }

    private static bool TryNormalizeTelefone(string value, out string digits)
    {
        digits = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length == 0
            || (trimmed.Contains('+') && trimmed[0] != '+')
            || trimmed.Count(character => character == '+') > 1
            || trimmed.Count(character => character == '(') != trimmed.Count(character => character == ')'))
        {
            return false;
        }

        var normalized = new char[trimmed.Length];
        var length = 0;
        foreach (var character in trimmed)
        {
            if (character is >= '0' and <= '9')
            {
                normalized[length++] = character;
            }
            else if (character is not '+' and not '(' and not ')' and not '-' && !char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        if (length == 0 || (trimmed[0] == '+' && !new string(normalized, 0, length).StartsWith("55", StringComparison.Ordinal)))
        {
            return false;
        }

        digits = new string(normalized, 0, length);
        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        return true;
    }

    private static bool IsBrazilianPhone(string digits)
    {
        if (digits.Length is not 10 and not 11)
        {
            return false;
        }

        var areaCode = digits.AsSpan(0, 2);
        var firstAreaDigit = areaCode[0] - '0';
        var secondAreaDigit = areaCode[1] - '0';
        if (firstAreaDigit is < 1 or > 9 || secondAreaDigit is < 1 or > 9)
        {
            return false;
        }

        var subscriber = digits.AsSpan(2);
        return subscriber.Length == 9
            ? subscriber[0] == '9'
            : subscriber[0] is >= '2' and <= '5';
    }

    private static void ValidateSenha(string? senha, List<CadastroClienteInputValidationError> erros)
    {
        if (string.IsNullOrEmpty(senha))
        {
            AddError(erros, nameof(CadastroClienteInput.Senha), "A senha é obrigatória.");
        }
        else if (senha.Length > SenhaMaxLength)
        {
            AddError(erros, nameof(CadastroClienteInput.Senha), "A senha excede o limite seguro de 256 caracteres.");
        }
    }

    private static void ValidateEndereco(
        EnderecoInicialInput? endereco,
        List<CadastroClienteInputValidationError> erros)
    {
        if (endereco is null)
        {
            AddError(erros, nameof(CadastroClienteInput.EnderecoInicial), "O endereço inicial é obrigatório.");
            return;
        }

        try
        {
            ClienteEndereco.Criar(
                Guid.NewGuid(),
                Guid.NewGuid(),
                endereco.Cep,
                endereco.Uf,
                endereco.Cidade,
                endereco.Bairro,
                endereco.Logradouro,
                endereco.Numero,
                endereco.Complemento,
                isDefault: true);
        }
        catch (ArgumentException exception)
        {
            AddEnderecoError(exception.ParamName, erros);
        }
    }

    private static void AddEnderecoError(
        string? parameterName,
        List<CadastroClienteInputValidationError> erros)
    {
        var field = parameterName switch
        {
            "cep" => nameof(EnderecoInicialInput.Cep),
            "uf" => nameof(EnderecoInicialInput.Uf),
            "cidade" => nameof(EnderecoInicialInput.Cidade),
            "bairro" => nameof(EnderecoInicialInput.Bairro),
            "logradouro" => nameof(EnderecoInicialInput.Logradouro),
            "numero" => nameof(EnderecoInicialInput.Numero),
            "complemento" => nameof(EnderecoInicialInput.Complemento),
            _ => nameof(CadastroClienteInput.EnderecoInicial)
        };

        var message = parameterName switch
        {
            "cep" => "O CEP deve conter oito dígitos.",
            "uf" => "A UF deve ser válida.",
            "cidade" => "A cidade é obrigatória e deve respeitar o limite de 100 caracteres.",
            "bairro" => "O bairro é obrigatório e deve respeitar o limite de 100 caracteres.",
            "logradouro" => "O logradouro é obrigatório e deve respeitar o limite de 150 caracteres.",
            "numero" => "O número é obrigatório e deve respeitar o limite de 20 caracteres.",
            "complemento" => "O complemento deve respeitar o limite de 100 caracteres.",
            _ => "O endereço inicial é inválido."
        };

        AddError(erros, $"EnderecoInicial.{field}", message);
    }

    private static void AddError(
        List<CadastroClienteInputValidationError> erros,
        string field,
        string message) =>
        erros.Add(new CadastroClienteInputValidationError(field, message));

    private static CadastroClienteInputValidationResult Result(
        List<CadastroClienteInputValidationError> erros) =>
        new(Array.AsReadOnly(erros.ToArray()));
}
