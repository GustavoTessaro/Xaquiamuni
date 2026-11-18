using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IConfirmarEntregaPedidoRepository
{
    async Task<ConfirmarEntregaPedidoResult> IConfirmarEntregaPedidoRepository.ConfirmarEntregaAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transacao = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            var variantes = await BloquearPedidoEVariantesAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            if (!await AdministradorAptoAsync(administradorId, agora, cancellationToken).ConfigureAwait(false))
            {
                return new(ConfirmarEntregaPedidoStatus.AdministradorNaoAutorizado);
            }

            var pedido = await _dbContext.Pedidos.Include(pedido => pedido.Itens)
                .SingleOrDefaultAsync(pedido => pedido.Id == pedidoId, cancellationToken).ConfigureAwait(false);
            if (pedido is null)
            {
                return new(ConfirmarEntregaPedidoStatus.PedidoNaoEncontrado);
            }
            if (pedido.Situacao is not (SituacaoPedido.ProntoParaRetirada or SituacaoPedido.Entregue))
            {
                return new(ConfirmarEntregaPedidoStatus.EstadoIncompativel);
            }

            var reservas = await CarregarReservasRetiradaAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            if (!ConfirmarEntregaPedido.ReservasCorrespondem(pedido, reservas))
            {
                return new(ConfirmarEntregaPedidoStatus.ReservasInconsistentes);
            }

            var anteriores = await CarregarMovimentosRetiradaAsync(reservas, cancellationToken).ConfigureAwait(false);
            if (pedido.Situacao == SituacaoPedido.Entregue)
            {
                return ConfirmarEntregaPedido.MovimentosCorrespondem(pedido, reservas, anteriores)
                    ? ResultadoEntrega(ConfirmarEntregaPedidoStatus.Repetida, pedido, anteriores)
                    : new(ConfirmarEntregaPedidoStatus.ReservasInconsistentes);
            }
            if (anteriores.Count != 0)
            {
                return new(ConfirmarEntregaPedidoStatus.ReservasInconsistentes);
            }

            var baixa = await PrepararBaixasRetiradaAsync(reservas, variantes, administradorId, cancellationToken)
                .ConfigureAwait(false);
            if (baixa.Status != ConsumirReservasStatus.Sucesso)
            {
                return new(baixa.Status switch
                {
                    ConsumirReservasStatus.LoteInelegivel => ConfirmarEntregaPedidoStatus.LoteInelegivel,
                    ConsumirReservasStatus.SaldoInsuficiente => ConfirmarEntregaPedidoStatus.SaldoInsuficiente,
                    _ => ConfirmarEntregaPedidoStatus.ReservasInconsistentes,
                });
            }

            var instante = baixa.Movimentos[0].RegistradoEmUtc;
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            pedido.ConfirmarEntrega(administradorId, instante);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultadoEntrega(ConfirmarEntregaPedidoStatus.Entregue, pedido, baixa.Movimentos);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ConfirmarEntregaPedidoStatus.ConflitoConcorrencia);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return new(ConfirmarEntregaPedidoStatus.ConflitoConcorrencia);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(ConfirmarEntregaPedidoStatus.ConflitoConcorrencia);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static ConfirmarEntregaPedidoResult ResultadoEntrega(
        ConfirmarEntregaPedidoStatus status, Pedido pedido, IReadOnlyList<MovimentoProdutoAcabado> movimentos) =>
        new(status, new(pedido.Id, pedido.EncerradoPorId!.Value, pedido.EncerradoEmUtc!.Value,
            movimentos.Select(movimento => movimento.Id).ToArray()));
}
