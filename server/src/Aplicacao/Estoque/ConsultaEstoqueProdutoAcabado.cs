using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public enum SituacaoEstoqueProdutoAcabado : byte
{
    SemEstoque = 1,
    AbaixoOuNoMinimo = 2,
    Normal = 3,
    MinimoNaoConfigurado = 4
}

public sealed record PaginacaoEstoqueProdutoAcabado(int Pagina = 1, int ItensPorPagina = 20)
{
    public const int TamanhoMaximoPagina = 100;

    public int CalcularSkip()
    {
        if (Pagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Pagina), "A página deve ser maior que zero.");
        }

        if (ItensPorPagina < 1 || ItensPorPagina > TamanhoMaximoPagina)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ItensPorPagina),
                $"O tamanho da página deve estar entre 1 e {TamanhoMaximoPagina}.");
        }

        try
        {
            return checked((Pagina - 1) * ItensPorPagina);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(Pagina), "A paginação excede o limite suportado.");
        }
    }
}

public sealed record ListarMovimentosProdutoAcabadoFiltro(
    Guid ProdutoVarianteId,
    TipoMovimentoProdutoAcabado? Tipo = null,
    Guid? LoteId = null,
    int Pagina = 1,
    int ItensPorPagina = 20,
    Guid? MovimentoId = null);

public sealed record PoliticaEstoqueProdutoVarianteConsulta(
    Guid ProdutoVarianteId,
    int? EstoqueMinimo,
    int? PrazoValidadePadraoDias);

public sealed record PosicaoLoteProdutoAcabadoConsulta(
    Guid LoteId,
    string Codigo,
    DateOnly DataFabricacao,
    DateOnly DataValidade,
    OrigemValidadeLoteProdutoAcabado OrigemValidade,
    int? PrazoValidadeAplicadoDias,
    bool Valido,
    bool TemRegistroSaldo,
    byte[]? Versao,
    int QuantidadeVendavel,
    int QuantidadeDanificada,
    int QuantidadeQuarentena,
    int SaldoFisico,
    int SaldoUtilizavel,
    int SaldoReservado,
    int SaldoDisponivel);

public sealed record ResumoEstoqueProdutoAcabadoConsulta(
    Guid ProdutoVarianteId,
    bool VarianteAtiva,
    int SaldoFisico,
    int SaldoUtilizavel,
    int SaldoReservado,
    int SaldoDisponivel,
    int? EstoqueMinimo,
    bool EstoqueMinimoConfigurado,
    SituacaoEstoqueProdutoAcabado Situacao);

public sealed record DetalheEstoqueProdutoAcabadoConsulta(
    PoliticaEstoqueProdutoVarianteConsulta? Politica,
    ResumoEstoqueProdutoAcabadoConsulta Resumo,
    IReadOnlyList<PosicaoLoteProdutoAcabadoConsulta> Lotes);

public sealed record MovimentoProdutoAcabadoConsulta(
    Guid MovimentoId,
    TipoMovimentoProdutoAcabado Tipo,
    int Quantidade,
    CondicaoEstoqueProdutoAcabado? CondicaoOrigem,
    CondicaoEstoqueProdutoAcabado? CondicaoDestino,
    int? QuantidadeContada,
    SaldoProdutoAcabadoSnapshot SaldoAnterior,
    SaldoProdutoAcabadoSnapshot SaldoPosterior,
    string Motivo,
    string? Observacao,
    Guid ResponsavelId,
    DateTimeOffset RegistradoEmUtc,
    Guid LoteId,
    string CodigoLote,
    Guid? MovimentoOriginalId);

public interface IEstoqueProdutoAcabadoConsultaRepository
{
    Task<DetalheEstoqueProdutoAcabadoConsulta?> ConsultarPosicaoVarianteAsync(
        Guid produtoVarianteId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default);

    Task<PosicaoLoteProdutoAcabadoConsulta?> ConsultarPosicaoLoteAsync(
        Guid produtoVarianteId,
        Guid loteId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default);

    Task<PaginaEstoque<MovimentoProdutoAcabadoConsulta>> ListarMovimentosAsync(
        ListarMovimentosProdutoAcabadoFiltro filtro,
        CancellationToken cancellationToken = default);
}

public interface IConsultarEstoqueProdutoAcabado
{
    Task<DetalheEstoqueProdutoAcabadoConsulta?> ConsultarPosicaoVarianteAsync(
        Guid produtoVarianteId,
        CancellationToken cancellationToken = default);

    Task<PosicaoLoteProdutoAcabadoConsulta?> ConsultarPosicaoLoteAsync(
        Guid produtoVarianteId,
        Guid loteId,
        CancellationToken cancellationToken = default);

    Task<PaginaEstoque<MovimentoProdutoAcabadoConsulta>> ListarMovimentosAsync(
        ListarMovimentosProdutoAcabadoFiltro filtro,
        CancellationToken cancellationToken = default);
}

public sealed class ConsultarEstoqueProdutoAcabado(
    IEstoqueProdutoAcabadoConsultaRepository repository,
    ICalendarioOperacionalEstoque calendario) : IConsultarEstoqueProdutoAcabado
{
    public Task<DetalheEstoqueProdutoAcabadoConsulta?> ConsultarPosicaoVarianteAsync(
        Guid produtoVarianteId,
        CancellationToken cancellationToken = default)
    {
        ValidarIdentificador(produtoVarianteId, nameof(produtoVarianteId));
        return repository.ConsultarPosicaoVarianteAsync(
            produtoVarianteId,
            calendario.ObterDataAtual(),
            cancellationToken);
    }

    public Task<PosicaoLoteProdutoAcabadoConsulta?> ConsultarPosicaoLoteAsync(
        Guid produtoVarianteId,
        Guid loteId,
        CancellationToken cancellationToken = default)
    {
        ValidarIdentificador(produtoVarianteId, nameof(produtoVarianteId));
        ValidarIdentificador(loteId, nameof(loteId));
        return repository.ConsultarPosicaoLoteAsync(
            produtoVarianteId,
            loteId,
            calendario.ObterDataAtual(),
            cancellationToken);
    }

    public Task<PaginaEstoque<MovimentoProdutoAcabadoConsulta>> ListarMovimentosAsync(
        ListarMovimentosProdutoAcabadoFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        _ = new PaginacaoEstoqueProdutoAcabado(filtro.Pagina, filtro.ItensPorPagina).CalcularSkip();
        ValidarIdentificador(filtro.ProdutoVarianteId, nameof(filtro.ProdutoVarianteId));
        if (filtro.Tipo is not null && !Enum.IsDefined(filtro.Tipo.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(filtro.Tipo), filtro.Tipo, "Tipo de movimento inválido.");
        }

        if (filtro.LoteId == Guid.Empty || filtro.MovimentoId == Guid.Empty)
        {
            throw new ArgumentException("Os identificadores do lote e movimento não podem ser vazios.", nameof(filtro));
        }

        return repository.ListarMovimentosAsync(filtro, cancellationToken);
    }

    private static void ValidarIdentificador(Guid identificador, string parametro)
    {
        if (identificador == Guid.Empty)
        {
            throw new ArgumentException("O identificador é obrigatório.", parametro);
        }
    }
}
