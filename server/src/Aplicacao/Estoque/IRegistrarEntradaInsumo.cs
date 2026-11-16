namespace Xaquiamuni.Aplicacao.Estoque;

public interface IRegistrarEntradaInsumo
{
    Task<RegistrarEntradaInsumoResult> ExecuteAsync(
        RegistrarEntradaInsumoInput? input,
        CancellationToken cancellationToken = default);
}
