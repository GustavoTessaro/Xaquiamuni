namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginAdministradorInput
{
    public string? Email { get; init; }
    public string? Senha { get; init; }

    public override string ToString() => "[LoginAdministradorInput]";
}
