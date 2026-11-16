namespace Xaquiamuni.Aplicacao.Estoque;

public interface IEstoqueInsumoRepository
{
    Task<RegistrarEntradaInsumoResult> RegistrarEntradaAsync(
        RegistrarEntradaInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<AtualizarPoliticaLoteInsumoStatus> AtualizarPoliticaLoteAsync(
        Guid insumoId,
        bool controlaLote,
        bool validadeObrigatoria,
        CancellationToken cancellationToken = default);
}
