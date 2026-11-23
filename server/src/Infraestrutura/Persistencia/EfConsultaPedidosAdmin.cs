using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed class EfConsultaPedidosAdmin(XaquiamuniDbContext dbContext) : IConsultarPedidosAdmin
{
    public async Task<PaginaPedidosAdmin> ListarAsync(
        SituacaoPedido? situacao, int deslocamento, int limite, CancellationToken cancellationToken)
    {
        var consulta = dbContext.Pedidos.AsNoTracking();
        if (situacao is { } filtro)
        {
            consulta = consulta.Where(pedido => pedido.Situacao == filtro);
        }

        var total = await consulta.CountAsync(cancellationToken).ConfigureAwait(false);
        var pedidos = await (
            from pedido in consulta
            join perfil in dbContext.ClientePerfis on pedido.ClienteId equals perfil.UserId
            orderby pedido.CriadoEmUtc descending, pedido.Id descending
            select new ResumoPedidoAdmin(pedido.Id, pedido.Situacao, pedido.CriadoEmUtc,
                pedido.PrazoPagamentoEmUtc, pedido.TotalCentavos, perfil.NomeCompleto, pedido.NumeroPublico)
        ).Skip(deslocamento).Take(limite).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new(pedidos, total);
    }

    public async Task<IReadOnlyList<CorrecaoRastreamentoAdmin>?> ConsultarHistoricoRastreamentoAsync(
        Guid pedidoId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Pedidos.AnyAsync(pedido => pedido.Id == pedidoId, cancellationToken))
            return null;

        return await dbContext.Set<OperacaoEntregaPostal>().AsNoTracking()
            .Where(operacao => operacao.PedidoId == pedidoId && operacao.Etapa == EtapaEntregaPostal.CorrigirRastreamento)
            .OrderByDescending(operacao => operacao.RealizadaEmUtc)
            .ThenBy(operacao => operacao.AdministradorId)
            .ThenBy(operacao => operacao.Chave)
            .Select(operacao => new CorrecaoRastreamentoAdmin(operacao.CodigoAnterior, operacao.CodigoNovo,
                operacao.RealizadaEmUtc, operacao.AdministradorId))
            .ToListAsync(cancellationToken);
    }

    public Task<DetalhePedidoAdmin?> BuscarAsync(Guid pedidoId, CancellationToken cancellationToken) =>
        (from pedido in dbContext.Pedidos.AsNoTracking().Include(pedido => pedido.Itens)
         join perfil in dbContext.ClientePerfis on pedido.ClienteId equals perfil.UserId
         join cliente in dbContext.Users on pedido.ClienteId equals cliente.Id
         where pedido.Id == pedidoId
         select new DetalhePedidoAdmin(pedido, perfil.NomeCompleto, cliente.Email))
        .SingleOrDefaultAsync(cancellationToken);
}
