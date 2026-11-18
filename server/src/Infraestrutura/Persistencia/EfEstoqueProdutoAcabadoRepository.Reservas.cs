using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IReservaProdutoAcabadoRepository
{
    private const string MensagemEstoqueComprometido =
        "A operação reduziria unidades vendáveis comprometidas por reservas comerciais.";

    public async Task<ReservarProdutoAcabadoResult> ReservarAsync(
        ReservarProdutoAcabadoInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Quantidade);
        try
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
            await AdquirirLockPedidoAsync(input.PedidoId, cancellationToken).ConfigureAwait(false);
            await AdquirirLockVarianteAsync(input.ProdutoVarianteId, cancellationToken).ConfigureAwait(false);

            var existentes = await (
                from reserva in _dbContext.ReservasProdutoAcabado.AsNoTracking()
                join lote in _dbContext.LotesProdutoAcabado on reserva.LoteId equals lote.Id
                where reserva.PedidoId == input.PedidoId && reserva.ProdutoVarianteId == input.ProdutoVarianteId
                orderby lote.DataValidade, lote.Codigo
                select reserva
            ).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (existentes.Count > 0)
            {
                return existentes.Sum(reserva => (long)reserva.Quantidade) == input.Quantidade
                    ? new(ReservarProdutoAcabadoStatus.Sucesso, existentes, Repetida: true)
                    : new(ReservarProdutoAcabadoStatus.ConflitoIdempotencia, []);
            }

            // A proteção da linha dura até o commit e serializa a alocação com a inativação no catálogo.
            var variante = await _dbContext.Database.SqlQuery<bool>($"""
                SELECT [Ativa] AS [Value] FROM [ProdutoVariantes] WITH (HOLDLOCK, INDEX([PK_ProdutoVariantes]))
                WHERE [Id] = {input.ProdutoVarianteId}
                """).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (variante.Count == 0)
            {
                return new(ReservarProdutoAcabadoStatus.VarianteNaoEncontrada, []);
            }
            if (!variante[0])
            {
                return new(ReservarProdutoAcabadoStatus.VarianteInativa, []);
            }

            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            var situacoesDoPedido = await _dbContext.ReservasProdutoAcabado
                .Where(reserva => reserva.PedidoId == input.PedidoId)
                .Select(reserva => new { reserva.Estado, reserva.ExpiraEmUtc })
                .Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
            var situacao = situacoesDoPedido.FirstOrDefault();
            if (situacoesDoPedido.Count > 1 || (situacao is not null
                && (situacao.Estado != EstadoReservaProdutoAcabado.Ativa || agora >= situacao.ExpiraEmUtc)))
            {
                return new(ReservarProdutoAcabadoStatus.PedidoForaDaAlocacao, []);
            }

            var dataOperacional = _calendario.ObterDataAtual();
            var comprometidoPorLote = _dbContext.ReservasProdutoAcabado
                .ComprometidasEm(agora)
                .Where(reserva => reserva.ProdutoVarianteId == input.ProdutoVarianteId)
                .GroupBy(reserva => reserva.LoteId)
                .Select(grupo => new { LoteId = grupo.Key, Quantidade = grupo.Sum(reserva => (long)reserva.Quantidade) });

            var lotes = await (
                from lote in _dbContext.LotesProdutoAcabado
                join saldo in _dbContext.SaldosProdutoAcabado
                    on new { lote.ProdutoVarianteId, LoteId = lote.Id } equals new { saldo.ProdutoVarianteId, saldo.LoteId }
                join comprometido in comprometidoPorLote on lote.Id equals comprometido.LoteId into compromissos
                from comprometido in compromissos.DefaultIfEmpty()
                where lote.ProdutoVarianteId == input.ProdutoVarianteId
                    && lote.DataValidade >= dataOperacional && saldo.QuantidadeVendavel > 0
                orderby lote.DataValidade, lote.Codigo
                select new
                {
                    lote.Id,
                    Disponivel = saldo.QuantidadeVendavel - ((long?)comprometido.Quantidade ?? 0)
                }
            ).ToListAsync(cancellationToken).ConfigureAwait(false);

            var reservas = new List<ReservaProdutoAcabado>();
            var falta = input.Quantidade;
            foreach (var lote in lotes)
            {
                if (lote.Disponivel <= 0)
                {
                    continue;
                }

                var quantidade = (int)Math.Min(falta, lote.Disponivel);
                reservas.Add(ReservaProdutoAcabado.Criar(
                    input.PedidoId, input.ProdutoVarianteId, lote.Id, quantidade, agora, situacao?.ExpiraEmUtc));
                falta -= quantidade;
                if (falta == 0)
                {
                    break;
                }
            }

            if (falta != 0)
            {
                return new(ReservarProdutoAcabadoStatus.SaldoInsuficiente, []);
            }

            _dbContext.ReservasProdutoAcabado.AddRange(reservas);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(ReservarProdutoAcabadoStatus.Sucesso, reservas);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return new(ReservarProdutoAcabadoStatus.ConflitoConcorrencia, []);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(ReservarProdutoAcabadoStatus.ConflitoConcorrencia, []);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private Task<bool> ReduzEstoqueComprometidoAsync(
        MovimentoProdutoAcabado movimento,
        CancellationToken cancellationToken) =>
        ReduzEstoqueComprometidoAsync(
            movimento.ProdutoVarianteId,
            movimento.LoteId,
            movimento.SaldoAnterior.QuantidadeVendavel,
            movimento.SaldoPosterior.QuantidadeVendavel,
            cancellationToken);

    private async Task<bool> ReduzEstoqueComprometidoAsync(
        Guid varianteId,
        Guid loteId,
        int quantidadeAnterior,
        int quantidadePosterior,
        CancellationToken cancellationToken)
    {
        if (quantidadePosterior >= quantidadeAnterior)
        {
            return false;
        }

        var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        var comprometido = await _dbContext.ReservasProdutoAcabado
            .ComprometidasEm(agora)
            .Where(reserva => reserva.ProdutoVarianteId == varianteId && reserva.LoteId == loteId)
            .SumAsync(reserva => (long)reserva.Quantidade, cancellationToken)
            .ConfigureAwait(false);
        return quantidadePosterior < comprometido;
    }
}

internal static class ConsultaReservasProdutoAcabado
{
    // Todos os escritores usam o mesmo relógio para avaliar os prazos das reservas.
    internal static Task<DateTimeOffset> ObterAgoraReservasUtcAsync(
        this XaquiamuniDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
            .SingleAsync(cancellationToken);

    internal static IQueryable<ReservaProdutoAcabado> ComprometidasEm(
        this IQueryable<ReservaProdutoAcabado> reservas,
        DateTimeOffset agora) =>
        reservas.Where(reserva => reserva.Estado == EstadoReservaProdutoAcabado.Confirmada
            || (reserva.Estado == EstadoReservaProdutoAcabado.Ativa && reserva.ExpiraEmUtc > agora));
}
