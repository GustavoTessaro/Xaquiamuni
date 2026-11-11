namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface ILoginCliente
{
    Task<LoginClienteResult> ExecuteAsync(
        LoginClienteInput? input,
        CancellationToken cancellationToken = default);
}
