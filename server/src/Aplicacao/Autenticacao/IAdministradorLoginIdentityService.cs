namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IAdministradorLoginIdentityService
{
    Task<LoginAdministradorStatus> SignInAdministradorAsync(
        string email,
        string senha,
        CancellationToken cancellationToken = default);
}
