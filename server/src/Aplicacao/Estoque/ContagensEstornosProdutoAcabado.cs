namespace Xaquiamuni.Aplicacao.Estoque;

public interface IContarProdutoAcabado
{
    Task<ContarProdutoAcabadoResult> ExecuteAsync(
        ContarProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class ContarProdutoAcabado(
    IEstoqueProdutoAcabadoRepository repository) : IContarProdutoAcabado
{
    public Task<ContarProdutoAcabadoResult> ExecuteAsync(
        ContarProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.ContarAsync(input, cancellationToken);
    }
}

public interface IEstornarMovimentoProdutoAcabado
{
    Task<EstornarMovimentoProdutoAcabadoResult> ExecuteAsync(
        EstornarMovimentoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class EstornarMovimentoProdutoAcabado(
    IEstoqueProdutoAcabadoRepository repository) : IEstornarMovimentoProdutoAcabado
{
    public Task<EstornarMovimentoProdutoAcabadoResult> ExecuteAsync(
        EstornarMovimentoProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.EstornarAsync(input, cancellationToken);
    }
}
