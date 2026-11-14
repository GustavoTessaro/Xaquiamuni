namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginClienteResult
{
    private LoginClienteResult(LoginClienteStatus status)
    {
        Status = status;
    }

    public LoginClienteStatus Status { get; }
    public bool Succeeded => Status == LoginClienteStatus.Succeeded;

    public static LoginClienteResult Success() =>
        new(LoginClienteStatus.Succeeded);

    public static LoginClienteResult Failure() =>
        new(LoginClienteStatus.InvalidCredentials);

    public static LoginClienteResult EmailNotConfirmed() =>
        new(LoginClienteStatus.EmailNotConfirmed);
}
