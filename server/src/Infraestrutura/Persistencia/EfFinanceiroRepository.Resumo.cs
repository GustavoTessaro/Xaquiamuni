using System.Globalization;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Financeiro;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfFinanceiroRepository
{
    private enum Indicador { Entradas, Saidas, AReceber, APagar, ValorAConferir }

    public async Task<ResumoFinanceiroDto> ResumirAsync(DateOnly? inicio, DateOnly? fim, CancellationToken ct)
    {
        var hoje = calendario.ObterDataAtual();
        var fluxo = NoPeriodo(MovimentosCaixa(), inicio, fim)
            .Select(m => new
            {
                Indicador = m.Tipo == TipoMovimentacaoFinanceira.Entrada ? Indicador.Entradas : Indicador.Saidas,
                Situacao = SituacaoVencimento.EmAberto,
                m.Valor
            });

        var aConferir = ComprasNaoCanceladas().Where(c => c.Total == null)
            .Select(c => new { Indicador = Indicador.ValorAConferir, Situacao = SituacaoVencimento.EmAberto, Valor = 1L });
        var contas = ClassificarContasEmAberto(hoje).Select(x => new
        {
            Indicador = x.Conta.Direcao == DirecaoConta.AReceber ? Indicador.AReceber : Indicador.APagar,
            x.Situacao,
            x.Conta.Valor
        });

        // Separe quociente e resto para que o CAST padrão do EF para decimal(18,2)
        // acomode qualquer valor bigint. SUM(decimal) acumula as partes sem o limite de bigint.
        const long baseSoma = 1000;
        var totais = await fluxo.Concat(contas).Concat(aConferir)
            .GroupBy(m => new { m.Indicador, m.Situacao })
            .Select(g => new
            {
                g.Key,
                Quantidade = g.LongCount(),
                Quociente = g.Sum(m => (decimal)(m.Valor / baseSoma)),
                Resto = g.Sum(m => (decimal)(m.Valor % baseSoma))
            })
            .ToDictionaryAsync(g => (g.Key.Indicador, g.Key.Situacao),
                g => (g.Quantidade, Valor: new BigInteger(g.Quociente) * baseSoma + new BigInteger(g.Resto)), ct);

        var entradas = totais.GetValueOrDefault((Indicador.Entradas, SituacaoVencimento.EmAberto)).Valor;
        var saidas = totais.GetValueOrDefault((Indicador.Saidas, SituacaoVencimento.EmAberto)).Valor;
        var pagar = DaDirecao(Indicador.APagar);
        var receber = DaDirecao(Indicador.AReceber);
        return new(
            new(inicio, fim, Texto(entradas), Texto(saidas), Texto(entradas - saidas)),
            new(receber.ValorCentavos, pagar.ValorCentavos,
                totais.GetValueOrDefault((Indicador.ValorAConferir, SituacaoVencimento.EmAberto)).Quantidade,
                new(hoje, pagar, receber)));

        IndicadoresDirecaoContaDto DaDirecao(Indicador indicador)
        {
            var normal = totais.GetValueOrDefault((indicador, SituacaoVencimento.EmAberto));
            var vencidas = totais.GetValueOrDefault((indicador, SituacaoVencimento.Vencida));
            var venceHoje = totais.GetValueOrDefault((indicador, SituacaoVencimento.VenceHoje));
            var proximas = totais.GetValueOrDefault((indicador, SituacaoVencimento.VencendoEmBreve));
            return new(normal.Quantidade + vencidas.Quantidade + venceHoje.Quantidade + proximas.Quantidade,
                Texto(normal.Valor + vencidas.Valor + venceHoje.Valor + proximas.Valor),
                new(vencidas.Quantidade, Texto(vencidas.Valor)),
                new(venceHoje.Quantidade, Texto(venceHoje.Valor)),
                new(proximas.Quantidade + venceHoje.Quantidade, Texto(proximas.Valor + venceHoje.Valor)));
        }
    }

    private static string Texto(BigInteger centavos) => centavos.ToString(CultureInfo.InvariantCulture);
}
