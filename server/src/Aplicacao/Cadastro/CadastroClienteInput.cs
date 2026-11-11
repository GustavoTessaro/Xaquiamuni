namespace Xaquiamuni.Aplicacao.Cadastro;

public sealed class CadastroClienteInput
{
    public string? NomeCompleto { get; init; }
    public string? Cpf { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? Senha { get; init; }
    public EnderecoInicialInput? EnderecoInicial { get; init; }

    public override string ToString() => "[CadastroClienteInput]";
}

public sealed class EnderecoInicialInput
{
    public string? Cep { get; init; }
    public string? Uf { get; init; }
    public string? Cidade { get; init; }
    public string? Bairro { get; init; }
    public string? Logradouro { get; init; }
    public string? Numero { get; init; }
    public string? Complemento { get; init; }

    public override string ToString() => "[EnderecoInicialInput]";
}
