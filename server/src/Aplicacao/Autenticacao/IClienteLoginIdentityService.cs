namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IClienteLoginIdentityService
{
    Task<LoginClienteStatus> SignInClienteAsync(
        string email,
        string senha,
        CancellationToken cancellationToken = default);
}
