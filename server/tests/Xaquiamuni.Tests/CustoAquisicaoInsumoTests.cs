using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class CustoAquisicaoInsumoTests
{
    [Fact]
    public void Criar_DistingueCustoDesconhecidoDeCustoZero()
    {
        var quantity = QuantidadeEstoque.Criar(2m, UnidadeFisicaEstoque.Quilograma);

        var unknown = CustoAquisicaoInsumo.Criar(null, quantity);
        var free = CustoAquisicaoInsumo.Criar(0m, quantity);

        Assert.Null(unknown.Total);
        Assert.Null(unknown.PorUnidadeBase);
        Assert.Equal(0m, free.Total);
        Assert.Equal(0m, free.PorUnidadeBase);
    }

    [Fact]
    public void Criar_CalculaCustoPorUnidadeBaseComEscalaDefinida()
    {
        var quantity = QuantidadeEstoque.Criar(500m, UnidadeFisicaEstoque.Grama);

        var cost = CustoAquisicaoInsumo.Criar(12.50m, quantity);

        Assert.Equal(12.50m, cost.Total);
        Assert.Equal(0.025m, cost.PorUnidadeBase);
        Assert.Equal(14, CustoAquisicaoInsumo.CustoUnitarioScale);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.001)]
    public void Criar_RejeitaCustoNegativoOuComMaisDeDuasCasas(decimal total)
    {
        var quantity = QuantidadeEstoque.Criar(1m, UnidadeFisicaEstoque.Unidade);

        Assert.Throws<ArgumentOutOfRangeException>(() => CustoAquisicaoInsumo.Criar(total, quantity));
    }
}
