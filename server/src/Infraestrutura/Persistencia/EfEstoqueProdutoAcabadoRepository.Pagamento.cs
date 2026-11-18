using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IConfirmarPagamentoPedidoRepository
{
    async Task<ConfirmarPagamentoPedidoStatus> IConfirmarPagamentoPedidoRepository.ConfirmarAsync(
        Guid pedidoId, Guid administradorId, CancellationToken cancellationToken)
    {
        try
        {
            // Mantém a associação ao papel ADMIN estável até a gravação da decisão.
            await using var transacao = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            await BloquearPedidoEVariantesAsync(pedidoId, cancellationToken).ConfigureAwait(false);

            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            if (!await AdministradorAptoAsync(administradorId, agora, cancellationToken).ConfigureAwait(false))
            {
                return ConfirmarPagamentoPedidoStatus.AdministradorNaoAutorizado;
            }

            var pedido = await _dbContext.Pedidos.Include(pedido => pedido.Itens)
                .SingleOrDefaultAsync(pedido => pedido.Id == pedidoId, cancellationToken).ConfigureAwait(false);
            if (pedido is null)
            {
                return ConfirmarPagamentoPedidoStatus.PedidoNaoEncontrado;
            }

            var reservas = await _dbContext.ReservasProdutoAcabado.Where(reserva => reserva.PedidoId == pedidoId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var resultado = ConfirmarPagamentoPedido.PrepararReservas(pedido, reservas, agora);
            if (resultado != ConfirmarPagamentoPedidoStatus.Confirmado)
            {
                return resultado;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            pedido.ConfirmarPagamento(administradorId, agora);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConfirmarPagamentoPedidoStatus.ConflitoConcorrencia;
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return ConfirmarPagamentoPedidoStatus.ConflitoConcorrencia;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return ConfirmarPagamentoPedidoStatus.ConflitoConcorrencia;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private Task<bool> AdministradorAptoAsync(Guid administradorId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        (from usuario in _dbContext.Users
         join papelUsuario in _dbContext.UserRoles on usuario.Id equals papelUsuario.UserId
         join papel in _dbContext.Roles on papelUsuario.RoleId equals papel.Id
         where usuario.Id == administradorId && papel.NormalizedName == XaquiamuniRoles.Administrador
            && (!usuario.LockoutEnabled || usuario.LockoutEnd == null || usuario.LockoutEnd < agora)
         select usuario.Id).AnyAsync(cancellationToken);
}
