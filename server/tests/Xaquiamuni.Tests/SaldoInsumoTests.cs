using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class SaldoInsumoTests
{
    [Fact]
    public void CreateZero_RepresentsAggregatedBalanceForUntrackedSupply()
    {
        var insumo = CriarInsumo(controlaLote: false);

        var saldo = SaldoInsumo.CriarZero(insumo);

        Assert.NotEqual(Guid.Empty, saldo.Id);
        Assert.Equal(insumo.Id, saldo.InsumoId);
        Assert.Null(saldo.LoteId);
        Assert.Equal(UnidadeFisicaEstoque.Grama, saldo.QuantidadeFisica.UnidadeBase);
        Assert.Equal(0m, saldo.QuantidadeFisica.ValorBase);
        Assert.Equal(0m, insumo.CalcularSaldoFisico([saldo]).ValorBase);
    }

    [Fact]
    public void CreateZero_RequiresLotIdentityOnlyForTrackedSupply()
    {
        var tracked = CriarInsumo(controlaLote: true);
        var untracked = CriarInsumo(controlaLote: false);
        var lot = LoteInsumo.Criar(tracked, "L-1", new DateOnly(2026, 11, 16));

        Assert.Equal(
            "lote",
            Assert.Throws<ArgumentException>(() => SaldoInsumo.CriarZero(tracked)).ParamName);
        Assert.Equal(
            "lote",
            Assert.Throws<ArgumentException>(() => SaldoInsumo.CriarZero(untracked, lot)).ParamName);
        Assert.Equal(
            "lote",
            Assert.Throws<ArgumentException>(() =>
                SaldoInsumo.CriarZero(tracked, LoteInsumo.Criar(
                    CriarInsumo(controlaLote: true),
                    "L-2",
                    new DateOnly(2026, 11, 16)))).ParamName);

        var lotSaldo = SaldoInsumo.CriarZero(tracked, lot);
        Assert.Equal(lot.Id, lotSaldo.LoteId);
        Assert.Equal(0m, tracked.CalcularSaldoFisico([lotSaldo]).ValorBase);
    }

    [Fact]
    public void PhysicalBalance_IsZeroForAbsentOrExplicitZeroRows()
    {
        var insumo = CriarInsumo(controlaLote: false);
        var explicitZero = SaldoInsumo.CriarZero(insumo);

        Assert.Equal(0m, insumo.CalcularSaldoFisico([]).ValorBase);
        Assert.Equal(0m, insumo.CalcularSaldoFisico([explicitZero]).ValorBase);
    }

    [Fact]
    public void PhysicalBalance_RejectsRowsFromAnotherSupplyAndDuplicateIdentity()
    {
        var insumo = CriarInsumo(controlaLote: false);
        var other = CriarInsumo(controlaLote: false);
        var first = SaldoInsumo.CriarZero(insumo);
        var second = SaldoInsumo.CriarZero(insumo);
        var foreign = SaldoInsumo.CriarZero(other);

        Assert.Throws<ArgumentException>(() => insumo.CalcularSaldoFisico([foreign]));
        Assert.Throws<InvalidOperationException>(() => insumo.CalcularSaldoFisico([first, second]));
    }

    private static InsumoEstoque CriarInsumo(bool controlaLote) =>
        InsumoEstoque.Criar(
            "Cera",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            controlaLote: controlaLote);
}
