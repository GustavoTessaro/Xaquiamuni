namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginAdministradorResult
{
    private LoginAdministradorResult(LoginAdministradorStatus status)
    {
        Status = status;
    }

    public LoginAdministradorStatus Status { get; }
    public bool Succeeded => Status == LoginAdministradorStatus.Succeeded;

    public static LoginAdministradorResult Success() =>
        new(LoginAdministradorStatus.Succeeded);

    public static LoginAdministradorResult Failure() =>
        new(LoginAdministradorStatus.InvalidCredentials);
}
