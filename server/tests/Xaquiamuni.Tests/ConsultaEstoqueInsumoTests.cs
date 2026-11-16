using Xaquiamuni.Aplicacao.Estoque;

namespace Xaquiamuni.Tests;

public sealed class ConsultaEstoqueInsumoTests
{
    public static TheoryData<decimal, decimal?, SituacaoEstoqueInsumo> Situacoes =>
        new()
        {
            { 0m, null, SituacaoEstoqueInsumo.SemEstoque },
            { 0m, 0m, SituacaoEstoqueInsumo.SemEstoque },
            { 1m, null, SituacaoEstoqueInsumo.MinimoNaoConfigurado },
            { 1m, 1m, SituacaoEstoqueInsumo.AbaixoOuNoMinimo },
            { 0.999999m, 1m, SituacaoEstoqueInsumo.AbaixoOuNoMinimo },
            { 1.000001m, 1m, SituacaoEstoqueInsumo.Normal }
        };

    [Theory]
    [MemberData(nameof(Situacoes))]
    public void Classificar_UsesAvailableStockAndTreatsEqualityAsAtMinimum(
        decimal saldoDisponivel,
        decimal? estoqueMinimo,
        SituacaoEstoqueInsumo esperado)
    {
        Assert.Equal(
            esperado,
            ClassificadorSituacaoEstoqueInsumo.Classificar(saldoDisponivel, estoqueMinimo));
    }

    [Fact]
    public void Paginacao_RejectsInvalidAndOverflowingRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaginacaoEstoqueInsumo(Pagina: 0).CalcularSkip());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaginacaoEstoqueInsumo(ItensPorPagina: PaginacaoEstoqueInsumo.TamanhoMaximoPagina + 1)
                .CalcularSkip());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaginacaoEstoqueInsumo(Pagina: int.MaxValue).CalcularSkip());
    }
}
