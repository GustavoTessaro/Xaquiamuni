namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginClienteResult
{
    private LoginClienteResult(bool succeeded)
    {
        Succeeded = succeeded;
    }

    public bool Succeeded { get; }

    public static LoginClienteResult Success() => new(true);

    public static LoginClienteResult Failure() => new(false);
}
