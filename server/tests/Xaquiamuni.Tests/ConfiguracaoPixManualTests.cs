using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class ConfiguracaoPixManualTests
{
    [Fact]
    public void NormalizaCamposSemGerarUmaChave()
    {
        var config = ConfiguracaoPixManual.Criar(TipoChavePixManual.Email, " Loja@EXAMPLE.TEST ", " Loja ", "  ");
        Assert.Equal("loja@example.test", config.ChavePix);
        Assert.Equal("Loja", config.NomeRecebedor);
        Assert.Null(config.InstrucoesAdicionais);
    }

    [Theory]
    [InlineData(TipoChavePixManual.Aleatoria, "5FBA3231-744A-4CC4-BC54-C958BB1CC890", "5fba3231-744a-4cc4-bc54-c958bb1cc890")]
    [InlineData(TipoChavePixManual.Telefone, "+5500000000000", "+5500000000000")]
    [InlineData(TipoChavePixManual.Cnpj, "99999999999962", "99999999999962")]
    [InlineData(TipoChavePixManual.Cnpj, "99.999.999/9999-62", "99999999999962")]
    [InlineData(TipoChavePixManual.Cnpj, "12ABC34501DE35", "12ABC34501DE35")]
    [InlineData(TipoChavePixManual.Cnpj, " 12.abc.345/01de-35 ", "12ABC34501DE35")]
    public void AceitaENormalizaChavesValidas(TipoChavePixManual tipo, string chave, string esperado)
    {
        var config = ConfiguracaoPixManual.Criar(tipo, chave, "Loja", null);
        Assert.Equal(tipo, config.TipoChave);
        Assert.Equal(esperado, config.ChavePix);
    }

    [Theory]
    [InlineData(TipoChavePixManual.Cnpj, "9999999999962")]
    [InlineData(TipoChavePixManual.Cnpj, "99999999999963")]
    [InlineData(TipoChavePixManual.Cnpj, "12ABC34501DE45")]
    [InlineData(TipoChavePixManual.Cnpj, "12ABC34501DEA5")]
    [InlineData(TipoChavePixManual.Cnpj, "12.ABC.345/01DÉ-35")]
    [InlineData(TipoChavePixManual.Cnpj, "12.ABC.345-01DE/35")]
    [InlineData(TipoChavePixManual.Cnpj, "00000000000000")]
    [InlineData(TipoChavePixManual.Aleatoria, "00000000-0000-0000-0000-000000000000")]
    [InlineData(TipoChavePixManual.Telefone, "11 1234-5678")]
    [InlineData(TipoChavePixManual.Email, "Nome <loja@example.test>")]
    [InlineData((TipoChavePixManual)99, "loja@example.test")]
    public void RejeitaTipoOuFormatoInvalido(TipoChavePixManual tipo, string chave) =>
        Assert.Throws<ArgumentException>(() => ConfiguracaoPixManual.Criar(tipo, chave, "Loja", null));
}
