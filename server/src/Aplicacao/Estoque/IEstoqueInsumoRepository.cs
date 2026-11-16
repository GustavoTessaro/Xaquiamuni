namespace Xaquiamuni.Aplicacao.Estoque;

public interface IEstoqueInsumoRepository
{
    Task<AdministrarInsumoEstoqueResult> CadastrarInsumoAsync(
        CadastrarInsumoEstoqueInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AtualizarInsumoAsync(
        AtualizarInsumoEstoqueInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AlterarStatusInsumoAsync(
        Guid insumoId,
        bool ativo,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> CadastrarFormaCompraAsync(
        CadastrarFormaCompraInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AtualizarFormaCompraAsync(
        AtualizarFormaCompraInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<AdministrarInsumoEstoqueResult> AlterarStatusFormaCompraAsync(
        Guid insumoId,
        Guid formaCompraId,
        bool ativa,
        CancellationToken cancellationToken = default);

    Task<RegistrarEntradaInsumoResult> RegistrarEntradaAsync(
        RegistrarEntradaInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<OperacaoEstoqueInsumoResult> RegistrarSaidaAsync(
        RegistrarSaidaInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<OperacaoEstoqueInsumoResult> AjustarContagemAsync(
        AjustarContagemInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<OperacaoEstoqueInsumoResult> EstornarMovimentoAsync(
        EstornarMovimentoInsumoInput input,
        CancellationToken cancellationToken = default);

    Task<AtualizarPoliticaLoteInsumoStatus> AtualizarPoliticaLoteAsync(
        Guid insumoId,
        bool controlaLote,
        bool validadeObrigatoria,
        CancellationToken cancellationToken = default);
}
