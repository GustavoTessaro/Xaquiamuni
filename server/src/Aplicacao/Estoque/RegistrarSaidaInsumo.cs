namespace Xaquiamuni.Aplicacao.Estoque;

public interface IRegistrarSaidaInsumo
{
    Task<OperacaoEstoqueInsumoResult> ExecutarAsync(
        RegistrarSaidaInsumoInput input,
        CancellationToken cancellationToken = default);
}

public sealed class RegistrarSaidaInsumo(IEstoqueInsumoRepository repository)
    : IRegistrarSaidaInsumo
{
    public Task<OperacaoEstoqueInsumoResult> ExecutarAsync(
        RegistrarSaidaInsumoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return repository.RegistrarSaidaAsync(input, cancellationToken);
    }
}
