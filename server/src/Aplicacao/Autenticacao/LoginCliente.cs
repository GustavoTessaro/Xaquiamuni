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

        var status = await _identityService.SignInClienteAsync(
            input.Email.Trim(),
            input.Senha,
            cancellationToken).ConfigureAwait(false);

        return status switch
        {
            LoginClienteStatus.Succeeded => LoginClienteResult.Success(),
            LoginClienteStatus.InvalidCredentials => LoginClienteResult.Failure(),
            LoginClienteStatus.EmailNotConfirmed => LoginClienteResult.EmailNotConfirmed(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                "The client login identity service returned an unsupported status.")
        };
    }
}
