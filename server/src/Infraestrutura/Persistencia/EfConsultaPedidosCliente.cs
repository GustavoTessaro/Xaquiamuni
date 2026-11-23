using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed class EfConsultaPedidosCliente(XaquiamuniDbContext dbContext) : IConsultarPedidosCliente
{
    public async Task<PaginaPedidosCliente> ListarAsync(
        Guid clienteId, int deslocamento, int limite, CancellationToken cancellationToken)
    {
        var consulta = PedidosDoCliente(clienteId);
        var total = await consulta.CountAsync(cancellationToken).ConfigureAwait(false);
        var pedidos = await consulta.OrderByDescending(pedido => pedido.CriadoEmUtc)
            .ThenByDescending(pedido => pedido.Id)
            .Skip(deslocamento).Take(limite)
            .Select(pedido => new ResumoPedidoCliente(pedido.Id, pedido.Situacao, pedido.CriadoEmUtc,
                pedido.PrazoPagamentoEmUtc, pedido.TotalCentavos, pedido.NumeroPublico))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(pedidos, total);
    }

    public Task<Pedido?> BuscarAsync(Guid clienteId, Guid pedidoId, CancellationToken cancellationToken) =>
        PedidosDoCliente(clienteId).Include(pedido => pedido.Itens)
            .SingleOrDefaultAsync(pedido => pedido.Id == pedidoId, cancellationToken);

    private IQueryable<Pedido> PedidosDoCliente(Guid clienteId) =>
        dbContext.Pedidos.AsNoTracking().Where(pedido => pedido.ClienteId == clienteId);
}
