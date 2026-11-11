namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginClienteInput
{
    public string? Email { get; init; }
    public string? Senha { get; init; }

    public override string ToString() => "[LoginClienteInput]";
}
