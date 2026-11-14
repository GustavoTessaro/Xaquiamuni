namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface ILoginAdministrador
{
    Task<LoginAdministradorResult> ExecuteAsync(
        LoginAdministradorInput? input,
        CancellationToken cancellationToken = default);
}
