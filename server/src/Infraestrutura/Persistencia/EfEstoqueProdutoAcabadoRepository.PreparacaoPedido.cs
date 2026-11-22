using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IPrepararPedidoCompraRepository
{
    async Task<PrepararPedidoStatus> IPrepararPedidoCompraRepository.AvancarAsync(
        Guid pedidoId, Guid administradorId, EtapaPreparacaoPedido etapa, CancellationToken cancellationToken)
    {
        try
        {
            await using var transacao = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            await BloquearPedidoEVariantesAsync(pedidoId, cancellationToken).ConfigureAwait(false);

            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            if (!await AdministradorAptoAsync(administradorId, agora, cancellationToken).ConfigureAwait(false))
            {
                return PrepararPedidoStatus.AdministradorNaoAutorizado;
            }

            var pedido = await _dbContext.Pedidos.Include(pedido => pedido.Itens)
                .SingleOrDefaultAsync(pedido => pedido.Id == pedidoId, cancellationToken).ConfigureAwait(false);
            if (pedido is null)
            {
                return PrepararPedidoStatus.PedidoNaoEncontrado;
            }
            if (etapa == EtapaPreparacaoPedido.LiberarParaRetirada &&
                pedido.ModalidadeEntrega != ModalidadeEntregaPedido.RetiradaLocal)
                return PrepararPedidoStatus.EstadoIncompativel;

            var reservas = await _dbContext.ReservasProdutoAcabado.AsNoTracking()
                .Where(reserva => reserva.PedidoId == pedidoId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var resultado = PrepararPedidoCompra.Aplicar(pedido, reservas, administradorId, agora, etapa);
            if (resultado == PrepararPedidoStatus.Sucesso)
            {
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PrepararPedidoStatus.ConflitoConcorrencia;
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return PrepararPedidoStatus.ConflitoConcorrencia;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return PrepararPedidoStatus.ConflitoConcorrencia;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }
}
