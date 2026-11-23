using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IConsumirReservasProdutoAcabado
{
    public async Task<ConsumirReservasResult> ConsumirAsync(
        Guid pedidoId, Guid responsavelId, CancellationToken cancellationToken = default)
    {
        if (pedidoId == Guid.Empty || responsavelId == Guid.Empty)
        {
            return new(ConsumirReservasStatus.SolicitacaoInvalida, []);
        }

        try
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            var variantes = await BloquearPedidoEVariantesAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            // A entrega de um pedido comercial deve coordenar estado e baixa na mesma transação.
            if (await PedidoComercialExisteAsync(pedidoId, cancellationToken).ConfigureAwait(false))
            {
                return new(ConsumirReservasStatus.EstadoIncompativel, []);
            }

            if (variantes.Count == 0)
            {
                return new(ConsumirReservasStatus.ReservasNaoEncontradas, []);
            }

            var reservas = await CarregarReservasRetiradaAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            var movimentosAnteriores = await CarregarMovimentosRetiradaAsync(reservas, cancellationToken).ConfigureAwait(false);
            if (reservas.All(reserva => reserva.Estado == EstadoReservaProdutoAcabado.Consumida)
                && movimentosAnteriores.Count == reservas.Count)
            {
                return new(ConsumirReservasStatus.Repetida, movimentosAnteriores);
            }

            if (movimentosAnteriores.Count != 0
                || reservas.Any(reserva => reserva.Estado != EstadoReservaProdutoAcabado.Confirmada
                    || reserva.ExpiraEmUtc != reservas[0].ExpiraEmUtc))
            {
                return new(ConsumirReservasStatus.EstadoIncompativel, []);
            }

            var baixa = await PrepararBaixasRetiradaAsync(reservas, variantes, responsavelId, cancellationToken)
                .ConfigureAwait(false);
            if (baixa.Status == ConsumirReservasStatus.Sucesso)
            {
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return baixa;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ConsumirReservasStatus.ConflitoConcorrencia, []);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return new(ConsumirReservasStatus.ConflitoConcorrencia, []);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(ConsumirReservasStatus.ConflitoConcorrencia, []);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private Task<List<ReservaProdutoAcabado>> CarregarReservasRetiradaAsync(
        Guid pedidoId, CancellationToken cancellationToken) =>
        _dbContext.ReservasProdutoAcabado.Where(reserva => reserva.PedidoId == pedidoId)
            .OrderBy(reserva => reserva.ProdutoVarianteId).ThenBy(reserva => reserva.LoteId).ThenBy(reserva => reserva.Id)
            .ToListAsync(cancellationToken);

    private Task<List<MovimentoProdutoAcabado>> CarregarMovimentosRetiradaAsync(
        IReadOnlyList<ReservaProdutoAcabado> reservas, CancellationToken cancellationToken)
    {
        var reservaIds = reservas.Select(reserva => (Guid?)reserva.Id).ToArray();
        return _dbContext.MovimentosProdutoAcabado.AsNoTracking()
            .Where(movimento => reservaIds.Contains(movimento.ReservaProdutoAcabadoId))
            .OrderBy(movimento => movimento.ProdutoVarianteId).ThenBy(movimento => movimento.LoteId)
            .ThenBy(movimento => movimento.ReservaProdutoAcabadoId)
            .ToListAsync(cancellationToken);
    }

    // O chamador detem os locks e coordena a gravacao e o commit.
    private async Task<ConsumirReservasResult> PrepararBaixasRetiradaAsync(
        IReadOnlyList<ReservaProdutoAcabado> reservas, IReadOnlyList<Guid> variantes,
        Guid responsavelId, CancellationToken cancellationToken, string motivo = "Retirada de pedido")
    {
        var variantesAtivas = new Dictionary<Guid, ProdutoVariante>();
        foreach (var varianteId in variantes)
        {
            // Mantém a linha protegida também contra atualizações do catálogo.
            var variante = await _dbContext.ProdutoVariantes.FromSqlInterpolated($"""
                SELECT * FROM [ProdutoVariantes] WITH (HOLDLOCK, INDEX([PK_ProdutoVariantes]))
                WHERE [Id] = {varianteId}
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (variante is null || !variante.Ativa)
            {
                return new(ConsumirReservasStatus.LoteInelegivel, []);
            }
            variantesAtivas.Add(varianteId, variante);
        }

        var agoraUtc = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        if (reservas.Any(reserva => reserva.ConfirmadaEmUtc is null || reserva.ConfirmadaEmUtc > agoraUtc))
        {
            return new(ConsumirReservasStatus.EstadoIncompativel, []);
        }
        var dataOperacional = _calendario.ObterDataEm(agoraUtc);
        var loteIds = reservas.Select(reserva => reserva.LoteId).Distinct().ToArray();
        var lotes = await _dbContext.LotesProdutoAcabado.AsNoTracking().Where(lote => loteIds.Contains(lote.Id))
            .ToDictionaryAsync(lote => lote.Id, cancellationToken).ConfigureAwait(false);
        if (lotes.Count != loteIds.Length || lotes.Values.Any(lote => !lote.EstaValidoEm(dataOperacional)))
        {
            return new(ConsumirReservasStatus.LoteInelegivel, []);
        }

        var saldos = await _dbContext.SaldosProdutoAcabado.Where(saldo => loteIds.Contains(saldo.LoteId))
            .ToDictionaryAsync(saldo => saldo.LoteId, cancellationToken).ConfigureAwait(false);
        // O saldo deve comportar também os compromissos de outros pedidos que continuam vigentes.
        var compromissos = await _dbContext.ReservasProdutoAcabado.ComprometidasEm(agoraUtc)
            .Where(reserva => loteIds.Contains(reserva.LoteId))
            .GroupBy(reserva => reserva.LoteId)
            .Select(grupo => new { Lote = grupo.Key, Quantidade = grupo.Sum(reserva => (long)reserva.Quantidade) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (saldos.Count != loteIds.Length || compromissos.Any(compromisso =>
            saldos[compromisso.Lote].QuantidadeVendavel < compromisso.Quantidade))
        {
            return new(ConsumirReservasStatus.SaldoInsuficiente, []);
        }

        var movimentos = reservas.Select(reserva => saldos[reserva.LoteId].RegistrarRetirada(
            reserva, variantesAtivas[reserva.ProdutoVarianteId], lotes[reserva.LoteId],
            dataOperacional, responsavelId, agoraUtc, motivo)).ToArray();
        _dbContext.MovimentosProdutoAcabado.AddRange(movimentos);
        return new(ConsumirReservasStatus.Sucesso, movimentos);
    }
}
