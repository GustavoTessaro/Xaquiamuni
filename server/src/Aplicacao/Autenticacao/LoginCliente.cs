namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginCliente : ILoginCliente
{
    private readonly IClienteLoginIdentityService _identityService;

    public LoginCliente(IClienteLoginIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<LoginClienteResult> ExecuteAsync(
        LoginClienteInput? input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input?.Email) || string.IsNullOrWhiteSpace(input.Senha))
        {
            return LoginClienteResult.Failure();
        }

        var succeeded = await _identityService.SignInClienteAsync(
            input.Email.Trim(),
            input.Senha,
            cancellationToken).ConfigureAwait(false);

        return succeeded ? LoginClienteResult.Success() : LoginClienteResult.Failure();
    }
}
