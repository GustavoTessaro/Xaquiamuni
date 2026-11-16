using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class LoteInsumoTests
{
    [Fact]
    public void Criar_NormalizesIdentificationAndKeepsOptionalSupplierAndExpiry()
    {
        var insumo = CriarInsumo(controlaLote: true);

        var lote = LoteInsumo.Criar(
            insumo,
            "  LOTE   2026-A ",
            new DateOnly(2026, 11, 16),
            new DateOnly(2027, 3, 10),
            "  Fornecedor   Local ");

        Assert.NotEqual(Guid.Empty, lote.Id);
        Assert.Equal(insumo.Id, lote.InsumoId);
        Assert.Equal("LOTE 2026-A", lote.Identificacao);
        Assert.Equal(new DateOnly(2026, 11, 16), lote.DataEntrada);
        Assert.Equal(new DateOnly(2027, 3, 10), lote.DataValidade);
        Assert.Equal("Fornecedor Local", lote.Fornecedor);
        Assert.False(lote.Bloqueado);
    }

    [Fact]
    public void Criar_AllowsMissingExpiryWhenNotRequiredAndSupplierIsOptional()
    {
        var insumo = CriarInsumo(controlaLote: true);

        var lote = LoteInsumo.Criar(
            insumo,
            "Lote sem validade",
            new DateOnly(2026, 11, 16));

        Assert.Null(lote.DataValidade);
        Assert.Null(lote.Fornecedor);
        Assert.True(lote.EstaElegivelEm(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Criar_RejectsMissingRequiredExpiryAndUntrackedSupply()
    {
        var requiresExpiry = CriarInsumo(controlaLote: true, validadeObrigatoria: true);
        var noLotControl = CriarInsumo(controlaLote: false);

        Assert.Equal(
            "dataValidade",
            Assert.Throws<ArgumentException>(() =>
                LoteInsumo.Criar(requiresExpiry, "Lote", new DateOnly(2026, 11, 16))).ParamName);
        Assert.Equal(
            "insumo",
            Assert.Throws<ArgumentException>(() =>
                LoteInsumo.Criar(noLotControl, "Lote", new DateOnly(2026, 11, 16))).ParamName);
    }

    [Fact]
    public void Eligibility_ExpiresTheDayAfterTheCalendarExpiryDate()
    {
        var insumo = CriarInsumo(controlaLote: true, validadeObrigatoria: true);
        var lote = LoteInsumo.Criar(
            insumo,
            "Lote com validade",
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 16));

        Assert.True(lote.EstaElegivelEm(new DateOnly(2026, 11, 15)));
        Assert.True(lote.EstaElegivelEm(new DateOnly(2026, 11, 16)));
        Assert.False(lote.EstaElegivelEm(new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public void BlockingChangesEligibilityWithoutChangingAnyBalance()
    {
        var insumo = CriarInsumo(controlaLote: true);
        var lote = LoteInsumo.Criar(insumo, "Lote", new DateOnly(2026, 11, 16));
        var saldo = SaldoInsumo.CriarZero(insumo, lote);

        lote.DefinirBloqueado(true);

        Assert.False(lote.EstaElegivelEm(new DateOnly(2026, 11, 16)));
        Assert.Equal(0m, saldo.QuantidadeFisica.ValorBase);
        lote.DefinirBloqueado(false);
        Assert.True(lote.EstaElegivelEm(new DateOnly(2026, 11, 16)));
    }

    [Fact]
    public void SupplierScopedCodesMayRepeatAndDoNotDefineInternalIdentity()
    {
        var insumo = CriarInsumo(controlaLote: true);

        var first = LoteInsumo.Criar(
            insumo,
            "A-100",
            new DateOnly(2026, 11, 16),
            fornecedor: "Fornecedor A");
        var second = LoteInsumo.Criar(
            insumo,
            "A-100",
            new DateOnly(2026, 11, 17),
            fornecedor: "Fornecedor B");

        Assert.Equal(first.Identificacao, second.Identificacao);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Criar_RejectsMissingIdentification(string? identificacao)
    {
        var insumo = CriarInsumo(controlaLote: true);

        Assert.Equal(
            "identificacao",
            Assert.Throws<ArgumentException>(() =>
                LoteInsumo.Criar(insumo, identificacao, new DateOnly(2026, 11, 16))).ParamName);
    }

    private static InsumoEstoque CriarInsumo(bool controlaLote, bool validadeObrigatoria = false) =>
        InsumoEstoque.Criar(
            "Óleo",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: controlaLote,
            validadeObrigatoria: validadeObrigatoria);
}
