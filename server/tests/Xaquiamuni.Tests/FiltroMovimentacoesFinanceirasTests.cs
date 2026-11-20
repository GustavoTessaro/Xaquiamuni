using Xaquiamuni.Aplicacao.Financeiro;

namespace Xaquiamuni.Tests;

public sealed class FiltroMovimentacoesFinanceirasTests
{
    [Fact]
    public void AceitaLimitesInclusivosEPeriodoAberto()
    {
        var data = new DateOnly(2026, 10, 2);
        var filtro = new FiltroMovimentacoesFinanceiras(data, data, TipoMovimentacaoFinanceira.Entrada,
            OrigemMovimentacaoFinanceira.PedidoSite, 1);
        Assert.True(filtro.Valido);
        Assert.True((filtro with { DataInicial = null }).Valido);
        Assert.True((filtro with { DataFinal = null, Pagina = 10000 }).Valido);
        Assert.False((filtro with { DataInicial = data.AddDays(1) }).Valido);
        Assert.False((filtro with { Tipo = (TipoMovimentacaoFinanceira)99 }).Valido);
        Assert.False((filtro with { Origem = (OrigemMovimentacaoFinanceira)99 }).Valido);
        Assert.False((filtro with { Pagina = 10001 }).Valido);
    }
}
