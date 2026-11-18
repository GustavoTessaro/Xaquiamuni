using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : ICicloReservasProdutoAcabado
{
    public Task<TransicaoReservasStatus> ConfirmarCompromissoAsync(
        Guid pedidoId, CancellationToken cancellationToken = default) =>
        TransicionarReservasAsync(pedidoId, TransicaoReservas.Confirmar, null, cancellationToken);

    public Task<TransicaoReservasStatus> LiberarAsync(
        Guid pedidoId, string? motivo, CancellationToken cancellationToken = default) =>
        TransicionarReservasAsync(pedidoId, TransicaoReservas.Liberar, motivo, cancellationToken);

    public Task<TransicaoReservasStatus> ExpirarAsync(
        Guid pedidoId, CancellationToken cancellationToken = default) =>
        TransicionarReservasAsync(pedidoId, TransicaoReservas.Expirar, null, cancellationToken);

    private async Task<TransicaoReservasStatus> TransicionarReservasAsync(
        Guid pedidoId, TransicaoReservas transicao, string? motivo, CancellationToken cancellationToken)
    {
        if (pedidoId == Guid.Empty || (transicao == TransicaoReservas.Liberar && string.IsNullOrWhiteSpace(motivo)))
        {
            return TransicaoReservasStatus.SolicitacaoInvalida;
        }

        try
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

            var reservasDoPedido = _dbContext.ReservasProdutoAcabado.Where(reserva => reserva.PedidoId == pedidoId);
            var variantes = await BloquearPedidoEVariantesAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            if (variantes.Count == 0)
            {
                return TransicaoReservasStatus.ReservasNaoEncontradas;
            }

            var reservas = await reservasDoPedido.ToListAsync(cancellationToken).ConfigureAwait(false);
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            var resultado = CicloReservasProdutoAcabado.Aplicar(reservas, transicao, agora, motivo);
            if (resultado != TransicaoReservasStatus.Sucesso)
            {
                return resultado;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransicaoReservasStatus.ConflitoConcorrencia;
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return TransicaoReservasStatus.ConflitoConcorrencia;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return TransicaoReservasStatus.ConflitoConcorrencia;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private Task AdquirirLockPedidoAsync(Guid pedidoId, CancellationToken cancellationToken) =>
        AdquirirLockAsync($"finished-order:{pedidoId:D}", cancellationToken);

    private async Task<List<Guid>> BloquearPedidoEVariantesAsync(Guid pedidoId, CancellationToken cancellationToken)
    {
        // A alocação também adquire primeiro o lock do pedido.
        await AdquirirLockPedidoAsync(pedidoId, cancellationToken).ConfigureAwait(false);
        var variantes = await _dbContext.ReservasProdutoAcabado
            .Where(reserva => reserva.PedidoId == pedidoId)
            .Select(reserva => reserva.ProdutoVarianteId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        variantes.Sort();
        foreach (var variante in variantes)
        {
            await AdquirirLockVarianteAsync(variante, cancellationToken).ConfigureAwait(false);
        }

        return variantes;
    }
}
