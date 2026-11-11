namespace Xaquiamuni.Aplicacao.Cadastro;

public interface ICadastrarCliente
{
    Task<CadastroClienteResult> ExecuteAsync(
        CadastroClienteInput? input,
        CancellationToken cancellationToken = default);
}
