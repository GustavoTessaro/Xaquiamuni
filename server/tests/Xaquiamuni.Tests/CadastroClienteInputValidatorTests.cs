using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Tests;

public class CadastroClienteInputValidatorTests
{
    private readonly CadastroClienteInputValidator _validator = new();

    [Fact]
    public void Validate_ComDadosValidosRetornaResultadoValido()
    {
        var result = _validator.Validate(ValidInput());

        Assert.True(result.Valido);
        Assert.Empty(result.Erros);
    }

    [Fact]
    public void Validate_ComCamposAusentesRetornaErrosPorCampo()
    {
        var result = _validator.Validate(new CadastroClienteInput());

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.NomeCompleto));
        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Cpf));
        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Email));
        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Telefone));
        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Senha));
        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.EnderecoInicial));
    }

    [Fact]
    public void Validate_RejeitaNomeAcimaDoLimiteDoDominio()
    {
        var input = ValidInput();
        input = new CadastroClienteInput
        {
            NomeCompleto = new string('A', 151),
            Cpf = input.Cpf,
            Email = input.Email,
            Telefone = input.Telefone,
            Senha = input.Senha,
            EnderecoInicial = input.EnderecoInicial
        };

        var result = _validator.Validate(input);

        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.NomeCompleto));
    }

    [Fact]
    public void Validate_RejeitaCpfInvalidoSemRevelarValor()
    {
        const string invalidCpf = "111.111.111-11";
        var input = WithCpf(ValidInput(), invalidCpf);

        var result = _validator.Validate(input);

        var error = Assert.Single(result.Erros, item => item.Campo == nameof(CadastroClienteInput.Cpf));
        Assert.DoesNotContain(invalidCpf, error.Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalido")]
    [InlineData("nome@dominio")]
    [InlineData("nome@@dominio.com")]
    public void Validate_RejeitaEmailInvalido(string email)
    {
        var result = _validator.Validate(WithEmail(ValidInput(), email));

        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Email));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("00 12345-6789")]
    [InlineData("+1 11 91234-5678")]
    [InlineData("11 61234-5678")]
    [InlineData("11 91234-567X")]
    public void Validate_RejeitaTelefoneBrasileiroInvalido(string telefone)
    {
        var result = _validator.Validate(WithPhone(ValidInput(), telefone));

        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Telefone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejeitaSenhaVazia(string? senha)
    {
        var result = _validator.Validate(WithPassword(ValidInput(), senha));

        Assert.Contains(result.Erros, error => error.Campo == nameof(CadastroClienteInput.Senha));
    }

    [Fact]
    public void Validate_AceitaSenhaNaoVaziaSemAnteciparPoliticaDoIdentity()
    {
        var result = _validator.Validate(WithPassword(ValidInput(), "   "));

        Assert.True(result.Valido);
    }

    [Fact]
    public void Validate_RejeitaSenhaAcimaDoLimite()
    {
        var password = new string('s', 257);
        var result = _validator.Validate(WithPassword(ValidInput(), password));

        var error = Assert.Single(result.Erros, item => item.Campo == nameof(CadastroClienteInput.Senha));
        Assert.DoesNotContain(password, error.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejeitaEnderecoInvalidoUsandoRegrasDoDominio()
    {
        var input = ValidInput();
        input = new CadastroClienteInput
        {
            NomeCompleto = input.NomeCompleto,
            Cpf = input.Cpf,
            Email = input.Email,
            Telefone = input.Telefone,
            Senha = input.Senha,
            EnderecoInicial = new EnderecoInicialInput
            {
                Cep = "12",
                Uf = "RS",
                Cidade = input.EnderecoInicial!.Cidade,
                Bairro = input.EnderecoInicial.Bairro,
                Logradouro = input.EnderecoInicial.Logradouro,
                Numero = input.EnderecoInicial.Numero
            }
        };

        var result = _validator.Validate(input);

        Assert.Contains(result.Erros, error => error.Campo == "EnderecoInicial.Cep");
    }

    [Fact]
    public void RepresentacoesTextuais_NaoExpoemCpfSenhaTelefoneOuEmail()
    {
        var input = ValidInput();
        var result = _validator.Validate(input);
        var error = Assert.Single(_validator.Validate(WithCpf(ValidInput(), "invalid")).Erros);

        Assert.Equal("[CadastroClienteInput]", input.ToString());
        Assert.Equal("[EnderecoInicialInput]", input.EnderecoInicial!.ToString());
        Assert.DoesNotContain(input.Cpf!, input.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input.Senha!, input.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input.Email!, input.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input.Telefone!, input.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input.Cpf!, error.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Senha!, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input.Cpf!, result.ToString(), StringComparison.Ordinal);
    }

    private static CadastroClienteInput ValidInput() => new()
    {
        NomeCompleto = "Ana Maria Silva",
        Cpf = "529.982.247-25",
        Email = "ana@example.com",
        Telefone = "+55 (11) 91234-5678",
        Senha = "Senha forte sem regras extras",
        EnderecoInicial = new EnderecoInicialInput
        {
            Cep = "95000-000",
            Uf = "RS",
            Cidade = "Vacaria",
            Bairro = "Centro",
            Logradouro = "Rua das Flores",
            Numero = "10",
            Complemento = "Casa 2"
        }
    };

    private static CadastroClienteInput WithCpf(CadastroClienteInput input, string? cpf) => new()
    {
        NomeCompleto = input.NomeCompleto,
        Cpf = cpf,
        Email = input.Email,
        Telefone = input.Telefone,
        Senha = input.Senha,
        EnderecoInicial = input.EnderecoInicial
    };

    private static CadastroClienteInput WithEmail(CadastroClienteInput input, string? email) => new()
    {
        NomeCompleto = input.NomeCompleto,
        Cpf = input.Cpf,
        Email = email,
        Telefone = input.Telefone,
        Senha = input.Senha,
        EnderecoInicial = input.EnderecoInicial
    };

    private static CadastroClienteInput WithPhone(CadastroClienteInput input, string? telefone) => new()
    {
        NomeCompleto = input.NomeCompleto,
        Cpf = input.Cpf,
        Email = input.Email,
        Telefone = telefone,
        Senha = input.Senha,
        EnderecoInicial = input.EnderecoInicial
    };

    private static CadastroClienteInput WithPassword(CadastroClienteInput input, string? senha) => new()
    {
        NomeCompleto = input.NomeCompleto,
        Cpf = input.Cpf,
        Email = input.Email,
        Telefone = input.Telefone,
        Senha = senha,
        EnderecoInicial = input.EnderecoInicial
    };
}
