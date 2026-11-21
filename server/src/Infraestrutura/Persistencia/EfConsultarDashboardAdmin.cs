using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Dashboard;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Agenda;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfConsultarDashboardAdmin(XaquiamuniDbContext db, ICalendarioOperacionalEstoque calendario)
    : IConsultarDashboardAdmin
{
    public async Task<OperacaoDashboard> OperacaoAsync(CancellationToken ct)
    {
        var agora = await db.ObterAgoraReservasUtcAsync(ct);
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.Situacao == SituacaoPedido.AguardandoPagamento ||
                        p.Situacao == SituacaoPedido.PagamentoConfirmado ||
                        p.Situacao == SituacaoPedido.EmPreparacao ||
                        p.Situacao == SituacaoPedido.ProntoParaRetirada)
            .GroupBy(p => new
            {
                p.Situacao,
                Vencido = p.Situacao == SituacaoPedido.AguardandoPagamento && p.PrazoPagamentoEmUtc <= agora,
            })
            .Select(g => new { g.Key.Situacao, g.Key.Vencido, Total = g.Count() })
            .ToDictionaryAsync(g => (g.Situacao, g.Vencido), g => g.Total, ct);

        var producoes = await db.OrdensProducao.CountAsync(o => o.Situacao == SituacaoOrdemProducao.EmAndamento, ct);
        var compras = await db.ComprasMateriasPrimas.CountAsync(c => c.Situacao == SituacaoCompraMateriaPrima.Rascunho, ct);
        return new(
            pedidos.GetValueOrDefault((SituacaoPedido.AguardandoPagamento, false)),
            pedidos.GetValueOrDefault((SituacaoPedido.AguardandoPagamento, true)),
            pedidos.GetValueOrDefault((SituacaoPedido.PagamentoConfirmado, false)),
            pedidos.GetValueOrDefault((SituacaoPedido.EmPreparacao, false)),
            pedidos.GetValueOrDefault((SituacaoPedido.ProntoParaRetirada, false)),
            producoes, compras);
    }

    public async Task<EstoqueDashboard> EstoqueAsync(CancellationToken ct)
    {
        var agora = await db.ObterAgoraReservasUtcAsync(ct);
        var hoje = calendario.ObterDataEm(agora);
        var variantes = EfCatalogoPublicoRepository.VariantesDisponiveis(db, agora, hoje);
        var produtos = await EfCatalogoPublicoRepository.FiltrarProdutosVisiveis(db.Produtos)
            .CountAsync(p => p.Variantes.Any(v => variantes.Contains(v.Id)), ct);
        var insumos = await new EfEstoqueInsumoConsultaRepository(db).ContarInsumosEmAlertaAsync(hoje, ct);
        return new(insumos, produtos);
    }

    public async Task<IReadOnlyList<CompromissoDashboard>> AgendaAsync(CancellationToken ct)
    {
        var hoje = calendario.ObterDataAtual();
        var fim = hoje.AddDays(30);
        return await db.Set<CompromissoAgenda>().AsNoTracking()
            .Where(c => c.ExcluidoEmUtc == null && c.Data >= hoje && c.Data <= fim)
            .OrderBy(c => c.Data).ThenBy(c => c.HorarioInicial).ThenBy(c => c.Id)
            .Select(c => new CompromissoDashboard(c.Data, c.HorarioInicial, c.Titulo))
            .Take(5).ToListAsync(ct);
    }
}
