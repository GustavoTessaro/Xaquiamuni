using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;
using Origem = Xaquiamuni.Aplicacao.Financeiro.OrigemMovimentacaoFinanceira;
using Tipo = Xaquiamuni.Aplicacao.Financeiro.TipoMovimentacaoFinanceira;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfFinanceiroRepository
{
    public async Task<PaginaMovimentacoesFinanceiras> ListarMovimentacoesAsync(
        FiltroMovimentacoesFinanceiras filtro, CancellationToken ct)
    {
        var movimentos = NoPeriodo(MovimentosCaixa(), filtro.DataInicial, filtro.DataFinal);
        if (filtro.Tipo is { } tipo) movimentos = movimentos.Where(m => m.Tipo == tipo);
        if (filtro.Origem is { } origem) movimentos = movimentos.Where(m => m.Origem == origem);

        const int limite = FiltroMovimentacoesFinanceiras.TamanhoPagina;
        var linhas = await movimentos.OrderByDescending(m => m.Data).ThenByDescending(m => m.EventoId)
            .ThenBy(m => m.Origem).Skip((filtro.Pagina - 1) * limite).Take(limite + 1).ToListAsync(ct);
        return new(filtro.Pagina, limite, linhas.Count > limite, linhas.Take(limite).Select(l => l.Dto()).ToArray());
    }

    private IQueryable<LinhaCaixa> MovimentosCaixa()
    {
        var pedidos = db.Pedidos.AsNoTracking()
            .Where(p => p.PagamentoConfirmadoEmUtc.HasValue &&
                (p.Situacao == SituacaoPedido.PagamentoConfirmado || p.Situacao == SituacaoPedido.EmPreparacao ||
                 p.Situacao == SituacaoPedido.ProntoParaRetirada || p.Situacao == SituacaoPedido.Entregue))
            .Select(p => new LinhaCaixa
            {
                EventoId = p.Id, RecursoId = p.Id, Origem = Origem.PedidoSite, Tipo = Tipo.Entrada,
                Data = DateOnly.FromDateTime(p.PagamentoConfirmadoEmUtc!.Value.Date),
                Valor = p.TotalCentavos, Meio = "Pix", Descricao = null
            });

        var vendas = from r in db.Set<RecebimentoVendaExterna>().AsNoTracking()
                     join v in db.Set<VendaExterna>() on r.VendaId equals v.Id
                     where v.Situacao == SituacaoVendaExterna.Registrada
                     select new LinhaCaixa
                     {
                         EventoId = r.Id, RecursoId = r.VendaId, Origem = Origem.VendaExterna, Tipo = Tipo.Entrada,
                         Data = r.Data, Valor = r.ValorCentavos, Descricao = null, Meio = r.Meio.ToString()
                     };

        var fornecedores = Pagamentos.AsNoTracking().Select(p => new LinhaCaixa
        {
            EventoId = p.Id, RecursoId = p.CompraId, Origem = Origem.PagamentoCompra, Tipo = Tipo.Saida,
            Data = p.Data, Valor = p.ValorCentavos, Descricao = null, Meio = p.Meio.ToString()
        });
        var manuais = db.Set<LancamentoFinanceiro>().AsNoTracking()
            .Where(l => l.CanceladoEmUtc == null && l.Data != null)
            .Select(l => new LinhaCaixa
            {
                EventoId = l.Id, RecursoId = l.Id, Origem = Origem.LancamentoManual,
                Tipo = l.Tipo == TipoLancamento.Entrada ? Tipo.Entrada : Tipo.Saida,
                Data = l.Data!.Value, Valor = l.ValorCentavos, Descricao = l.Descricao, Meio = l.Meio!.Value.ToString()
            });

        return pedidos.Concat(vendas).Concat(fornecedores).Concat(manuais);
    }

    private static IQueryable<LinhaCaixa> NoPeriodo(IQueryable<LinhaCaixa> movimentos, DateOnly? inicio, DateOnly? fim)
    {
        if (inicio.HasValue) movimentos = movimentos.Where(m => m.Data >= inicio.Value);
        if (fim.HasValue) movimentos = movimentos.Where(m => m.Data <= fim.Value);
        return movimentos;
    }

    private sealed class LinhaCaixa
    {
        public Guid EventoId { get; init; }
        public Guid RecursoId { get; init; }
        public Origem Origem { get; init; }
        public Tipo Tipo { get; init; }
        public DateOnly Data { get; init; }
        public long Valor { get; init; }
        public string? Descricao { get; init; }
        public string? Meio { get; init; }

        public MovimentacaoFinanceiraDto Dto() =>
            new(EventoId, RecursoId, Tipo.ToString(), Origem.ToString(), Data,
                Origem == Origem.PedidoSite ? "ConfirmacaoUtc" : "Efetiva", Valor.ToString(CultureInfo.InvariantCulture),
                Descricao ?? (Origem switch
                {
                    Origem.PedidoSite => "Pagamento de pedido do site",
                    Origem.VendaExterna => "Recebimento de venda externa",
                    _ => "Pagamento a fornecedor"
                }), Meio);
    }
}
