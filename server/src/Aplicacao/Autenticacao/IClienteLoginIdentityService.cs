namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IClienteLoginIdentityService
{
    Task<bool> SignInClienteAsync(
        string email,
        string senha,
        CancellationToken cancellationToken = default);
}
