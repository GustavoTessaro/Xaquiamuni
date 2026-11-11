namespace Xaquiamuni.Aplicacao.Cadastro;

public interface IClienteTransactionFactory
{
    Task<IClienteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
