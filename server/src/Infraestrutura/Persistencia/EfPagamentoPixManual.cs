using System.Data;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed class EfPagamentoPixManual(XaquiamuniDbContext db) : IPagamentoPixManual
{
    public Task<ConfiguracaoPixManual?> ConsultarConfiguracaoAsync(CancellationToken cancellationToken) =>
        db.ConfiguracoesPixManual.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public async Task SalvarAsync(ConfiguracaoPixManual configuracao, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var atual = await db.ConfiguracoesPixManual.FromSqlRaw(
            "SELECT * FROM [ConfiguracoesPixManual] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 1")
            .SingleOrDefaultAsync(cancellationToken);
        if (atual is null)
        {
            db.ConfiguracoesPixManual.Add(configuracao);
        }
        else
        {
            db.Entry(atual).CurrentValues.SetValues(configuracao);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
    }

    public async Task<ConsultaPixPedido?> ConsultarPedidoAsync(
        Guid pedidoId, Guid clienteId, CancellationToken cancellationToken)
    {
        var consulta = await (
            from pedido in db.Pedidos.AsNoTracking()
            where pedido.Id == pedidoId && pedido.ClienteId == clienteId
            from agora in db.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
            let permitido = pedido.Situacao == SituacaoPedido.AguardandoPagamento && pedido.PrazoPagamentoEmUtc > agora
            from configuracao in db.ConfiguracoesPixManual.AsNoTracking().Where(_ => permitido).DefaultIfEmpty()
            select new ConsultaPixPedido(pedido.TotalCentavos, pedido.PrazoPagamentoEmUtc, permitido, configuracao)
        ).SingleOrDefaultAsync(cancellationToken);
        return consulta;
    }
}
