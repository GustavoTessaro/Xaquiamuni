namespace Xaquiamuni.Aplicacao.Estoque;

public interface IAjustarContagemInsumo
{
    Task<OperacaoEstoqueInsumoResult> ExecutarAsync(
        AjustarContagemInsumoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class AjustarContagemInsumo(IEstoqueInsumoRepository repository)
    : IAjustarContagemInsumo
{
    public Task<OperacaoEstoqueInsumoResult> ExecutarAsync(
        AjustarContagemInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.AjustarContagemAsync(input, cancellationToken);
    }
}
