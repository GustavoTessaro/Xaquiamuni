using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfEstoqueInsumoConsultaRepository(XaquiamuniDbContext dbContext)
    : IEstoqueInsumoConsultaRepository
{
    public async Task<PaginaEstoque<ResumoEstoqueInsumo>> ListarInsumosAsync(
        ListarInsumosEstoqueFiltro filtro,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var skip = new PaginacaoEstoqueInsumo(filtro.Pagina, filtro.ItensPorPagina).CalcularSkip();
        var consulta = ConstruirResumoQuery(dataReferencia);

        if (!string.IsNullOrWhiteSpace(filtro.Nome))
        {
            var nome = filtro.Nome.Trim();
            consulta = consulta.Where(item => item.Insumo.Nome.Contains(nome));
        }

        if (filtro.Ativo is not null)
        {
            consulta = consulta.Where(item => item.Insumo.Ativo == filtro.Ativo.Value);
        }

        consulta = AplicarFiltroSituacao(consulta, filtro.Situacao);
        var totalItens = await consulta.CountAsync(cancellationToken).ConfigureAwait(false);
        var itensSql = await consulta
            .OrderBy(item => item.Insumo.NomeNormalizado)
            .ThenBy(item => item.Insumo.Id)
            .Skip(skip)
            .Take(filtro.ItensPorPagina)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var itens = itensSql.Select(MapearResumo).ToArray();

        return new PaginaEstoque<ResumoEstoqueInsumo>(
            itens,
            totalItens,
            filtro.Pagina,
            filtro.ItensPorPagina);
    }

    public async Task<DetalheEstoqueInsumo?> ConsultarInsumoAsync(
        Guid insumoId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken = default)
    {
        if (insumoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do insumo é obrigatório.", nameof(insumoId));
        }

        var resumoSql = await ConstruirResumoQuery(dataReferencia)
            .SingleOrDefaultAsync(item => item.Insumo.Id == insumoId, cancellationToken)
            .ConfigureAwait(false);
        if (resumoSql is null)
        {
            return null;
        }

        var resumo = MapearResumo(resumoSql);
        var formasCompra = await dbContext.FormasCompraInsumo
            .AsNoTracking()
            .Where(forma => forma.InsumoId == insumoId)
            .OrderByDescending(forma => forma.Ativa)
            .ThenBy(forma => forma.NomeNormalizado)
            .ThenBy(forma => forma.Id)
            .Select(forma => new FormaCompraInsumoConsulta(
                forma.Id,
                forma.Nome,
                forma.QuantidadeEquivalentePorEmbalagem.ValorBase,
                forma.QuantidadeEquivalentePorEmbalagem.UnidadeBase,
                forma.Ativa))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var posicoes = resumoSql.Insumo.ControlaLote
            ? await ConsultarPosicoesPorLoteAsync(insumoId, dataReferencia, cancellationToken).ConfigureAwait(false)
            : await ConsultarPosicaoAgregadaAsync(insumoId, cancellationToken).ConfigureAwait(false);

        return new DetalheEstoqueInsumo(resumo, formasCompra, posicoes);
    }

    public async Task<IReadOnlyList<FormaCompraInsumoConsulta>?> ConsultarFormasCompraAsync(
        Guid insumoId,
        CancellationToken cancellationToken = default)
    {
        var existe = await dbContext.InsumosEstoque
            .AsNoTracking()
            .AnyAsync(item => item.Id == insumoId, cancellationToken)
            .ConfigureAwait(false);
        if (!existe)
        {
            return null;
        }

        return await dbContext.FormasCompraInsumo
            .AsNoTracking()
            .Where(forma => forma.InsumoId == insumoId)
            .OrderByDescending(forma => forma.Ativa)
            .ThenBy(forma => forma.NomeNormalizado)
            .ThenBy(forma => forma.Id)
            .Select(forma => new FormaCompraInsumoConsulta(
                forma.Id,
                forma.Nome,
                forma.QuantidadeEquivalentePorEmbalagem.ValorBase,
                forma.QuantidadeEquivalentePorEmbalagem.UnidadeBase,
                forma.Ativa))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaginaEstoque<MovimentoInsumoConsulta>> ListarMovimentosAsync(
        ListarMovimentosInsumoFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (filtro.InsumoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do insumo é obrigatório.", nameof(filtro));
        }

        var skip = new PaginacaoEstoqueInsumo(filtro.Pagina, filtro.ItensPorPagina).CalcularSkip();
        var movimentos = dbContext.MovimentosInsumo
            .AsNoTracking()
            .Where(movimento => movimento.InsumoId == filtro.InsumoId);
        if (filtro.Tipo is not null)
        {
            movimentos = movimentos.Where(movimento => movimento.Tipo == filtro.Tipo.Value);
        }

        if (filtro.LoteId is not null)
        {
            movimentos = movimentos.Where(movimento => movimento.LoteId == filtro.LoteId.Value);
        }

        if (filtro.MovimentoId is not null)
        {
            movimentos = movimentos.Where(movimento => movimento.Id == filtro.MovimentoId.Value);
        }

        var totalItens = await movimentos.CountAsync(cancellationToken).ConfigureAwait(false);
        var itens = await (
                from movimento in movimentos
                join lote in dbContext.LotesInsumo.AsNoTracking()
                    on new { movimento.InsumoId, movimento.LoteId }
                    equals new { lote.InsumoId, LoteId = (Guid?)lote.Id }
                    into lotes
                from lote in lotes.DefaultIfEmpty()
                orderby movimento.RegistradoEmUtc descending, movimento.Id descending
                select new MovimentoInsumoConsulta(
                    movimento.Id,
                    movimento.Tipo,
                    movimento.Direcao,
                    movimento.QuantidadeOriginal,
                    movimento.UnidadeOriginal,
                    movimento.QuantidadeBase.ValorBase,
                    movimento.QuantidadeBase.UnidadeBase,
                    movimento.SaldoAnterior.ValorBase,
                    movimento.SaldoPosterior.ValorBase,
                    movimento.Motivo,
                    movimento.Observacao,
                    movimento.Tipo == TipoMovimentoInsumo.AjusteInventario
                        ? OrigemMovimentoInsumo.ContagemFisica
                        : movimento.Tipo == TipoMovimentoInsumo.Estorno
                            ? OrigemMovimentoInsumo.MovimentoOriginal
                            : movimento.Tipo == TipoMovimentoInsumo.EstoqueInicial
                                || movimento.Tipo == TipoMovimentoInsumo.Aquisicao
                                ? movimento.FormaCompraId == null
                                    ? OrigemMovimentoInsumo.UnidadeFisica
                                    : OrigemMovimentoInsumo.FormaCompra
                                : OrigemMovimentoInsumo.OperacaoManual,
                    movimento.ResponsavelId,
                    movimento.RegistradoEmUtc,
                    movimento.LoteId,
                    lote == null ? null : lote.Identificacao,
                    movimento.FormaCompraId,
                    movimento.FormaCompraNome,
                    movimento.FatorCompra == null
                        ? null
                        : (decimal?)movimento.FatorCompra.ValorBase,
                    movimento.FatorCompra == null
                        ? null
                        : (UnidadeFisicaEstoque?)movimento.FatorCompra.UnidadeBase,
                    movimento.MovimentoOriginalId))
            .Skip(skip)
            .Take(filtro.ItensPorPagina)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PaginaEstoque<MovimentoInsumoConsulta>(
            itens,
            totalItens,
            filtro.Pagina,
            filtro.ItensPorPagina);
    }

    private IQueryable<ResumoEstoqueSql> ConstruirResumoQuery(DateOnly dataReferencia)
    {
        var reservasAtivas = dbContext.Ativas();
        var lotesElegiveis = dbContext.LotesInsumo.AsNoTracking()
            .Where(l => !l.Bloqueado && (l.DataValidade ?? dataReferencia) >= dataReferencia);
        return dbContext.InsumosEstoque
            .AsNoTracking()
            .Select(insumo => new ResumoEstoqueSql
            {
                Insumo = insumo,
                SaldoFisico = dbContext.SaldosInsumo
                    .AsNoTracking()
                    .Where(saldo => saldo.InsumoId == insumo.Id)
                    .Sum(saldo => (decimal?)saldo.QuantidadeFisica.ValorBase) ?? 0m,
                SaldoUtilizavel = dbContext.SaldosInsumo
                    .AsNoTracking()
                    .Where(saldo =>
                        saldo.InsumoId == insumo.Id
                        && ((!insumo.ControlaLote && saldo.LoteId == null)
                            || (insumo.ControlaLote
                                && saldo.LoteId != null
                                && dbContext.LotesInsumo.AsNoTracking().Any(lote =>
                                    lote.InsumoId == saldo.InsumoId
                                    && lote.Id == saldo.LoteId
                                    && !lote.Bloqueado
                                    && (lote.DataValidade ?? dataReferencia) >= dataReferencia))))
                    .Sum(saldo => (decimal?)saldo.QuantidadeFisica.ValorBase) ?? 0m,
                SaldoReservado = reservasAtivas.Where(a => a.Necessidade.InsumoId == insumo.Id)
                    .Sum(a => (decimal?)(a.Reservada - a.Consumida)) ?? 0m,
                ReservadoElegivel = reservasAtivas.Where(a => a.Necessidade.InsumoId == insumo.Id &&
                        ((!insumo.ControlaLote && a.Saldo.LoteId == null) || (insumo.ControlaLote &&
                            lotesElegiveis.Any(l => l.Id == a.Saldo.LoteId))))
                    .Sum(a => (decimal?)(a.Reservada - a.Consumida)) ?? 0m
            });
    }

    private static IQueryable<ResumoEstoqueSql> AplicarFiltroSituacao(
        IQueryable<ResumoEstoqueSql> consulta,
        SituacaoEstoqueInsumo? situacao) =>
        situacao switch
        {
            SituacaoEstoqueInsumo.SemEstoque =>
                consulta.Where(item => item.SaldoUtilizavel == 0m),
            SituacaoEstoqueInsumo.AbaixoOuNoMinimo =>
                consulta.Where(item =>
                    item.SaldoUtilizavel > 0m
                    && item.Insumo.EstoqueMinimo != null
                    && item.SaldoUtilizavel <= item.Insumo.EstoqueMinimo.ValorBase),
            SituacaoEstoqueInsumo.Normal =>
                consulta.Where(item =>
                    item.SaldoUtilizavel > 0m
                    && item.Insumo.EstoqueMinimo != null
                    && item.SaldoUtilizavel > item.Insumo.EstoqueMinimo.ValorBase),
            SituacaoEstoqueInsumo.MinimoNaoConfigurado =>
                consulta.Where(item =>
                    item.SaldoUtilizavel > 0m
                    && item.Insumo.EstoqueMinimo == null),
            _ => consulta
        };

    private async Task<IReadOnlyList<PosicaoEstoqueInsumoConsulta>> ConsultarPosicaoAgregadaAsync(
        Guid insumoId,
        CancellationToken cancellationToken)
    {
        var saldo = await dbContext.SaldosInsumo
            .AsNoTracking()
            .Where(item => item.InsumoId == insumoId && item.LoteId == null)
            .Select(item => new
            {
                item.QuantidadeFisica.ValorBase,
                Versao = EF.Property<byte[]>(item, "Versao"),
                Reservado = dbContext.Ativas().Where(a => a.SaldoId == item.Id)
                    .Sum(a => (decimal?)(a.Reservada - a.Consumida)) ?? 0m
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var quantidade = saldo?.ValorBase ?? 0m;
        var reservado = saldo?.Reservado ?? 0m;
        return
        [
            new PosicaoEstoqueInsumoConsulta(
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                saldo is not null,
                saldo?.Versao,
                quantidade,
                quantidade,
                reservado,
                Math.Max(0m, quantidade - reservado))
        ];
    }

    private async Task<IReadOnlyList<PosicaoEstoqueInsumoConsulta>> ConsultarPosicoesPorLoteAsync(
        Guid insumoId,
        DateOnly dataReferencia,
        CancellationToken cancellationToken)
    {
        var posicoes = await (
                from lote in dbContext.LotesInsumo.AsNoTracking()
                    .Where(lote => lote.InsumoId == insumoId)
                join saldo in dbContext.SaldosInsumo.AsNoTracking()
                    .Where(saldo => saldo.InsumoId == insumoId)
                    on (Guid?)lote.Id equals saldo.LoteId into saldos
                from saldo in saldos.DefaultIfEmpty()
                orderby lote.DataValidade, lote.DataEntrada, lote.Identificacao, lote.Id
                select new PosicaoLoteSql(
                    lote.Id,
                    lote.Identificacao,
                    lote.Fornecedor,
                    lote.DataEntrada,
                    lote.DataValidade,
                    lote.Bloqueado,
                    !lote.Bloqueado
                        && (lote.DataValidade ?? dataReferencia) >= dataReferencia,
                    saldo != null,
                    saldo == null ? null : EF.Property<byte[]>(saldo, "Versao"),
                    saldo == null ? 0m : saldo.QuantidadeFisica.ValorBase,
                    dbContext.Ativas().Where(a => saldo != null && a.SaldoId == saldo.Id)
                        .Sum(a => (decimal?)(a.Reservada - a.Consumida)) ?? 0m))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return posicoes.Select(posicao => new PosicaoEstoqueInsumoConsulta(
                posicao.LoteId,
                posicao.Identificacao,
                posicao.Fornecedor,
                posicao.DataEntrada,
                posicao.DataValidade,
                posicao.Bloqueado,
                posicao.Elegivel,
                posicao.TemRegistroSaldo,
                posicao.Versao,
                posicao.SaldoFisico,
                posicao.Elegivel ? posicao.SaldoFisico : 0m,
                posicao.Reservado,
                posicao.Elegivel ? Math.Max(0m, posicao.SaldoFisico - posicao.Reservado) : 0m))
            .ToArray();
    }

    private static ResumoEstoqueInsumo MapearResumo(ResumoEstoqueSql item) =>
        new(
            item.Insumo.Id,
            item.Insumo.Nome,
            item.Insumo.Tipo,
            item.Insumo.UnidadeBase,
            item.Insumo.Ativo,
            item.Insumo.ControlaLote,
            item.Insumo.ValidadeObrigatoria,
            item.SaldoFisico,
            item.SaldoUtilizavel,
            item.SaldoReservado,
            Math.Max(0m, item.SaldoUtilizavel - item.ReservadoElegivel),
            item.Insumo.EstoqueMinimo?.ValorBase,
            item.Insumo.EstoqueMinimo is not null,
            ClassificadorSituacaoEstoqueInsumo.Classificar(
                item.SaldoUtilizavel,
                item.Insumo.EstoqueMinimo?.ValorBase));

    private sealed class ResumoEstoqueSql
    {
        public InsumoEstoque Insumo { get; init; } = null!;
        public decimal SaldoFisico { get; init; }
        public decimal SaldoUtilizavel { get; init; }
        public decimal SaldoReservado { get; init; }
        public decimal ReservadoElegivel { get; init; }
    }

    private sealed record PosicaoLoteSql(
        Guid LoteId,
        string Identificacao,
        string? Fornecedor,
        DateOnly DataEntrada,
        DateOnly? DataValidade,
        bool Bloqueado,
        bool Elegivel,
        bool TemRegistroSaldo,
        byte[]? Versao,
        decimal SaldoFisico,
        decimal Reservado);
}
