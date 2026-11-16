namespace Xaquiamuni.Aplicacao.Estoque;

public interface IEstornarMovimentoInsumo
{
    Task<OperacaoEstoqueInsumoResult> ExecutarAsync(
        EstornarMovimentoInsumoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class EstornarMovimentoInsumo(IEstoqueInsumoRepository repository)
    : IEstornarMovimentoInsumo
{
    public Task<OperacaoEstoqueInsumoResult> ExecutarAsync(
        EstornarMovimentoInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.EstornarMovimentoAsync(input, cancellationToken);
    }
}
