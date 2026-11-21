using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfFinanceiroRepository
{
    public async Task<PaginaContasEmAberto> ListarContasEmAbertoAsync(FiltroContasEmAberto filtro, CancellationToken ct)
    {
        var hoje = calendario.ObterDataAtual();
        var consulta = ClassificarContasEmAberto(hoje);
        if (filtro.Direcao is { } direcao) consulta = consulta.Where(x => x.Conta.Direcao == direcao);
        if (filtro.Origem is { } origem) consulta = consulta.Where(x => x.Conta.Origem == origem);
        consulta = filtro.Classificacao switch
        {
            FiltroVencimento.Vencidas => consulta.Where(x => x.Situacao == SituacaoVencimento.Vencida),
            FiltroVencimento.VenceHoje => consulta.Where(x => x.Situacao == SituacaoVencimento.VenceHoje),
            FiltroVencimento.ProximosTresDias => consulta.Where(x => x.Situacao == SituacaoVencimento.VencendoEmBreve),
            FiltroVencimento.VencendoEmBreve => consulta.Where(x =>
                x.Situacao == SituacaoVencimento.VenceHoje || x.Situacao == SituacaoVencimento.VencendoEmBreve),
            _ => consulta
        };

        const int limite = FiltroContasEmAberto.TamanhoPagina;
        var linhas = await consulta.OrderBy(x => x.Conta.Vencimento == null).ThenBy(x => x.Conta.Vencimento)
            .ThenBy(x => x.Conta.Origem).ThenBy(x => x.Conta.Id)
            .Skip((filtro.Pagina - 1) * limite).Take(limite + 1).ToListAsync(ct);
        return new(hoje, filtro.Pagina, limite, linhas.Count > limite, linhas.Take(limite).Select(x => x.Dto()).ToArray());
    }

    private IQueryable<CompraMateriaPrima> ComprasNaoCanceladas() =>
        db.ComprasMateriasPrimas.AsNoTracking().Where(c => c.Situacao != SituacaoCompraMateriaPrima.Cancelada);

    private IQueryable<ContaClassificada> ClassificarContasEmAberto(DateOnly hoje)
    {
        var limite = hoje.AddDays(Math.Min(3, DateOnly.MaxValue.DayNumber - hoje.DayNumber));
        var manuais = Pendentes(db.Set<LancamentoFinanceiro>().AsNoTracking()).Select(l => new LinhaConta
        {
            Id = l.Id, Origem = OrigemConta.Manual,
            Direcao = l.Tipo == TipoLancamento.Entrada ? DirecaoConta.AReceber : DirecaoConta.APagar,
            Descricao = l.Descricao, Contraparte = l.Contraparte, Valor = l.ValorCentavos, Vencimento = l.Vencimento
        });
        var compras = ComprasNaoCanceladas().Where(c => c.Total.HasValue).Select(c => new LinhaConta
        {
            Id = c.Id, Origem = OrigemConta.Compra, Direcao = DirecaoConta.APagar,
            Descricao = "Compra de matérias-primas", Contraparte = c.NomeFornecedor,
            Valor = (long)(c.Total!.Value * 100m) - Pagamentos.Where(p => p.CompraId == c.Id).Sum(p => p.ValorCentavos),
            Vencimento = null
        });
        var vendas = db.Set<VendaExterna>().AsNoTracking().Where(v => v.Situacao == SituacaoVendaExterna.Registrada)
            .Select(v => new LinhaConta
            {
                Id = v.Id, Origem = OrigemConta.VendaExterna, Direcao = DirecaoConta.AReceber,
                Descricao = "Venda externa", Contraparte = v.Comprador,
                Valor = v.TotalCentavos - v.Recebimentos.Sum(r => r.ValorCentavos), Vencimento = v.Vencimento
            });
        return manuais.Concat(compras).Concat(vendas).Where(x => x.Valor > 0)
            .Select(conta => new ContaClassificada
            {
                Conta = conta,
                Situacao = conta.Vencimento < hoje ? SituacaoVencimento.Vencida :
                    conta.Vencimento == hoje ? SituacaoVencimento.VenceHoje :
                    conta.Vencimento <= limite ? SituacaoVencimento.VencendoEmBreve : SituacaoVencimento.EmAberto
            });
    }

    private sealed class LinhaConta
    {
        public Guid Id { get; init; }
        public DirecaoConta Direcao { get; init; }
        public OrigemConta Origem { get; init; }
        public string Descricao { get; init; } = null!;
        public string? Contraparte { get; init; }
        public long Valor { get; init; }
        public DateOnly? Vencimento { get; init; }
    }

    private sealed class ContaClassificada
    {
        public LinhaConta Conta { get; init; } = null!;
        public SituacaoVencimento Situacao { get; init; }

        public ContaEmAbertoDto Dto() => new(Conta.Id, Conta.Direcao.ToString(), Conta.Origem.ToString(), Conta.Descricao,
            Conta.Contraparte, Texto(Conta.Valor), Conta.Vencimento, Situacao.ToString(), Conta.Origem switch
            {
                OrigemConta.Compra => $"/api/admin/financeiro/compras/{Conta.Id:D}",
                OrigemConta.VendaExterna => $"/api/admin/vendas-externas/{Conta.Id:D}",
                _ => $"/api/admin/financeiro/lancamentos/{Conta.Id:D}"
            });
    }
}
