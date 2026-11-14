namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class LoginAdministrador : ILoginAdministrador
{
    private readonly IAdministradorLoginIdentityService _identityService;

    public LoginAdministrador(IAdministradorLoginIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<LoginAdministradorResult> ExecuteAsync(
        LoginAdministradorInput? input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input?.Email) || string.IsNullOrWhiteSpace(input.Senha))
        {
            return LoginAdministradorResult.Failure();
        }

        var status = await _identityService.SignInAdministradorAsync(
            input.Email.Trim(),
            input.Senha,
            cancellationToken).ConfigureAwait(false);

        return status switch
        {
            LoginAdministradorStatus.Succeeded => LoginAdministradorResult.Success(),
            LoginAdministradorStatus.InvalidCredentials => LoginAdministradorResult.Failure(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                "The administrator login identity service returned an unsupported status.")
        };
    }
}
