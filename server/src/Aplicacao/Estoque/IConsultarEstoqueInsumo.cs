namespace Xaquiamuni.Aplicacao.Estoque;

public interface IEstoqueInsumoConsultaRepository
{
    Task<PaginaEstoque<ResumoEstoqueInsumo>> ListarInsumosAsync(
        ListarInsumosEstoqueFiltro filtro,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default);

    Task<DetalheEstoqueInsumo?> ConsultarInsumoAsync(
        Guid insumoId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormaCompraInsumoConsulta>?> ConsultarFormasCompraAsync(
        Guid insumoId,
        CancellationToken cancellationToken = default);

    Task<PaginaEstoque<MovimentoInsumoConsulta>> ListarMovimentosAsync(
        ListarMovimentosInsumoFiltro filtro,
        CancellationToken cancellationToken = default);
}

public interface IConsultarEstoqueInsumo
{
    Task<PaginaEstoque<ResumoEstoqueInsumo>> ListarInsumosAsync(
        ListarInsumosEstoqueFiltro filtro,
        CancellationToken cancellationToken = default);

    Task<DetalheEstoqueInsumo?> ConsultarInsumoAsync(
        Guid insumoId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormaCompraInsumoConsulta>?> ConsultarFormasCompraAsync(
        Guid insumoId,
        CancellationToken cancellationToken = default);

    Task<PaginaEstoque<MovimentoInsumoConsulta>> ListarMovimentosAsync(
        ListarMovimentosInsumoFiltro filtro,
        CancellationToken cancellationToken = default);
}

public sealed class ConsultarEstoqueInsumo(
    IEstoqueInsumoConsultaRepository repository,
    ICalendarioOperacionalEstoque calendario) : IConsultarEstoqueInsumo
{
    public Task<PaginaEstoque<ResumoEstoqueInsumo>> ListarInsumosAsync(
        ListarInsumosEstoqueFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        _ = new PaginacaoEstoqueInsumo(filtro.Pagina, filtro.ItensPorPagina).CalcularSkip();
        ValidarSituacao(filtro.Situacao);
        return repository.ListarInsumosAsync(
            filtro,
            calendario.ObterDataAtual(),
            cancellationToken);
    }

    public Task<DetalheEstoqueInsumo?> ConsultarInsumoAsync(
        Guid insumoId,
        CancellationToken cancellationToken = default)
    {
        if (insumoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do insumo é obrigatório.", nameof(insumoId));
        }

        return repository.ConsultarInsumoAsync(
            insumoId,
            calendario.ObterDataAtual(),
            cancellationToken);
    }

    public Task<IReadOnlyList<FormaCompraInsumoConsulta>?> ConsultarFormasCompraAsync(
        Guid insumoId,
        CancellationToken cancellationToken = default)
    {
        if (insumoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do insumo é obrigatório.", nameof(insumoId));
        }

        return repository.ConsultarFormasCompraAsync(insumoId, cancellationToken);
    }

    public Task<PaginaEstoque<MovimentoInsumoConsulta>> ListarMovimentosAsync(
        ListarMovimentosInsumoFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        _ = new PaginacaoEstoqueInsumo(filtro.Pagina, filtro.ItensPorPagina).CalcularSkip();
        if (filtro.InsumoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do insumo é obrigatório.", nameof(filtro));
        }

        if (filtro.Tipo is not null && !Enum.IsDefined(filtro.Tipo.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(filtro.Tipo), filtro.Tipo, "Tipo de movimento inválido.");
        }

        if (filtro.LoteId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do lote não pode ser vazio.", nameof(filtro));
        }

        if (filtro.MovimentoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do movimento não pode ser vazio.", nameof(filtro));
        }

        return repository.ListarMovimentosAsync(filtro, cancellationToken);
    }

    private static void ValidarSituacao(SituacaoEstoqueInsumo? situacao)
    {
        if (situacao is not null && !Enum.IsDefined(situacao.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(situacao), situacao, "Situação de estoque inválida.");
        }
    }
}
