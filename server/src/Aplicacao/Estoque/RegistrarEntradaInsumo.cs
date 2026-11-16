namespace Xaquiamuni.Aplicacao.Estoque;

public sealed class RegistrarEntradaInsumo(IEstoqueInsumoRepository repository)
    : IRegistrarEntradaInsumo
{
    public Task<RegistrarEntradaInsumoResult> ExecuteAsync(
        RegistrarEntradaInsumoInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Task.FromResult(RegistrarEntradaInsumoResult.Failure(
                RegistrarEntradaInsumoStatus.EntradaInvalida,
                "Os dados da entrada são obrigatórios."));
        }

        return repository.RegistrarEntradaAsync(input, cancellationToken);
    }
}
