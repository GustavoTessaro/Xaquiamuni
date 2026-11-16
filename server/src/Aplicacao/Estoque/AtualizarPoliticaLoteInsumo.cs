namespace Xaquiamuni.Aplicacao.Estoque;

public sealed class AtualizarPoliticaLoteInsumo(IEstoqueInsumoRepository repository)
    : IAtualizarPoliticaLoteInsumo
{
    public Task<AtualizarPoliticaLoteInsumoStatus> ExecuteAsync(
        Guid insumoId,
        bool controlaLote,
        bool validadeObrigatoria,
        CancellationToken cancellationToken = default) =>
        repository.AtualizarPoliticaLoteAsync(
            insumoId,
            controlaLote,
            validadeObrigatoria,
            cancellationToken);
}
