namespace Xaquiamuni.Aplicacao.Estoque;

public interface IAtualizarPoliticaLoteInsumo
{
    Task<AtualizarPoliticaLoteInsumoStatus> ExecuteAsync(
        Guid insumoId,
        bool controlaLote,
        bool validadeObrigatoria,
        CancellationToken cancellationToken = default);
}
