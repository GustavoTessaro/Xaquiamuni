using System.Globalization;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfFinanceiroRepository
{
    private enum Indicador { Entradas, Saidas, AReceber, APagar, ValorAConferir }

    public async Task<ResumoFinanceiroDto> ResumirAsync(DateOnly? inicio, DateOnly? fim, CancellationToken ct)
    {
        var fluxo = NoPeriodo(MovimentosCaixa(), inicio, fim)
            .Select(m => new
            {
                Indicador = m.Tipo == TipoMovimentacaoFinanceira.Entrada ? Indicador.Entradas : Indicador.Saidas,
                m.Valor
            });

        var aReceber = db.Set<VendaExterna>().AsNoTracking()
            .Where(v => v.Situacao == SituacaoVendaExterna.Registrada)
            .Select(v => v.TotalCentavos - v.Recebimentos.Sum(r => r.ValorCentavos))
            .Where(saldo => saldo > 0)
            .Select(saldo => new { Indicador = Indicador.AReceber, Valor = saldo });

        var compras = db.ComprasMateriasPrimas.AsNoTracking().Where(c => c.Situacao != SituacaoCompraMateriaPrima.Cancelada);
        var aPagar = compras.Where(c => c.Total.HasValue)
            .Select(c => (long)(c.Total!.Value * 100m) -
                Pagamentos.Where(p => p.CompraId == c.Id).Sum(p => p.ValorCentavos))
            .Where(saldo => saldo > 0)
            .Select(saldo => new { Indicador = Indicador.APagar, Valor = saldo });
        var aConferir = compras.Where(c => c.Total == null)
            .Select(c => new { Indicador = Indicador.ValorAConferir, Valor = 1L });

        // Separe quociente e resto para que o CAST padrão do EF para decimal(18,2)
        // acomode qualquer valor bigint. SUM(decimal) acumula as partes sem o limite de bigint.
        const long baseSoma = 1000;
        var totais = await fluxo.Concat(aReceber).Concat(aPagar).Concat(aConferir)
            .GroupBy(m => m.Indicador)
            .Select(g => new
            {
                Indicador = g.Key,
                Quociente = g.Sum(m => (decimal)(m.Valor / baseSoma)),
                Resto = g.Sum(m => (decimal)(m.Valor % baseSoma))
            })
            .ToDictionaryAsync(g => g.Indicador, g => new BigInteger(g.Quociente) * baseSoma + new BigInteger(g.Resto), ct);

        var entradas = totais.GetValueOrDefault(Indicador.Entradas);
        var saidas = totais.GetValueOrDefault(Indicador.Saidas);
        return new(
            new(inicio, fim, Texto(entradas), Texto(saidas), Texto(entradas - saidas)),
            new(Texto(totais.GetValueOrDefault(Indicador.AReceber)), Texto(totais.GetValueOrDefault(Indicador.APagar)),
                (long)totais.GetValueOrDefault(Indicador.ValorAConferir)));
    }

    private static string Texto(BigInteger centavos) => centavos.ToString(CultureInfo.InvariantCulture);
}
