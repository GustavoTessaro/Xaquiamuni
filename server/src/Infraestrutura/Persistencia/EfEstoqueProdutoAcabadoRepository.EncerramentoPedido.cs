using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IEncerrarPedidoCompraRepository
{
    Task<EncerrarPedidoStatus> IEncerrarPedidoCompraRepository.ExpirarAsync(
        Guid pedidoId, CancellationToken cancellationToken) =>
        EncerrarPedidoAsync(pedidoId, null, null, cancellationToken);

    Task<EncerrarPedidoStatus> IEncerrarPedidoCompraRepository.CancelarAsync(
        Guid pedidoId, Guid administradorId, string motivo, CancellationToken cancellationToken) =>
        EncerrarPedidoAsync(pedidoId, administradorId, motivo, cancellationToken);

    async Task<LotePedidosVencidos> IEncerrarPedidoCompraRepository.ProcessarLoteAsync(
        int limite, CursorPedidosVencidos? apos, CancellationToken cancellationToken)
    {
        var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
        var fimVarredura = apos?.LimiteVarreduraUtc is { } limiteUtc && limiteUtc < agora ? limiteUtc : agora;
        var consulta = _dbContext.Pedidos.AsNoTracking()
            .Where(pedido => pedido.Situacao == SituacaoPedido.AguardandoPagamento
                && pedido.PrazoPagamentoEmUtc <= fimVarredura);
        if (apos is not null)
        {
            consulta = consulta.Where(pedido => pedido.PrazoPagamentoEmUtc > apos.PrazoPagamentoEmUtc
                || (pedido.PrazoPagamentoEmUtc == apos.PrazoPagamentoEmUtc && pedido.Id.CompareTo(apos.PedidoId) > 0));
        }

        var candidatos = await consulta.OrderBy(pedido => pedido.PrazoPagamentoEmUtc).ThenBy(pedido => pedido.Id)
            .Select(pedido => new CursorPedidosVencidos(pedido.PrazoPagamentoEmUtc, pedido.Id))
            .Take(limite + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

        var resultados = new List<ResultadoEncerramentoPedido>();
        foreach (var candidato in candidatos.Take(limite))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EncerrarPedidoStatus status;
            try
            {
                status = await EncerrarPedidoAsync(candidato.PedidoId, null, null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DbUpdateException or SqlException)
            {
                // O pedido teve rollback (ou commit de resultado incerto); a próxima passagem é segura.
                status = EncerrarPedidoStatus.FalhaPersistencia;
            }

            resultados.Add(new(candidato.PedidoId, status));
        }

        return new(resultados, candidatos.Count > limite
            ? candidatos[limite - 1] with { LimiteVarreduraUtc = fimVarredura }
            : null);
    }

    private async Task<EncerrarPedidoStatus> EncerrarPedidoAsync(
        Guid pedidoId, Guid? administradorId, string? motivo, CancellationToken cancellationToken)
    {
        try
        {
            await using var transacao = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            await BloquearPedidoEVariantesAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            if (administradorId is Guid ator
                && !await AdministradorAptoAsync(ator, agora, cancellationToken).ConfigureAwait(false))
            {
                return EncerrarPedidoStatus.AdministradorNaoAutorizado;
            }

            var pedido = await _dbContext.Pedidos.Include(pedido => pedido.Itens)
                .SingleOrDefaultAsync(pedido => pedido.Id == pedidoId, cancellationToken).ConfigureAwait(false);
            if (pedido is null)
            {
                return EncerrarPedidoStatus.PedidoNaoEncontrado;
            }

            var reservas = await _dbContext.ReservasProdutoAcabado.Where(reserva => reserva.PedidoId == pedidoId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var resultado = EncerrarPedidoCompra.PrepararReservas(pedido, reservas, agora, motivo);
            if (resultado is not (EncerrarPedidoStatus.Expirado or EncerrarPedidoStatus.Cancelado))
            {
                return resultado;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (administradorId is Guid responsavel)
            {
                pedido.Cancelar(responsavel, agora, motivo);
            }
            else
            {
                pedido.Expirar(agora);
            }
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EncerrarPedidoStatus.ConflitoConcorrencia;
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return EncerrarPedidoStatus.ConflitoConcorrencia;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return EncerrarPedidoStatus.ConflitoConcorrencia;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }
}
