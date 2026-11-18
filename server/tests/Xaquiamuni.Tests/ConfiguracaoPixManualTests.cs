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
    [InlineData(TipoChavePixManual.Aleatoria, "00000000-0000-0000-0000-000000000000")]
    [InlineData(TipoChavePixManual.Telefone, "11 1234-5678")]
    [InlineData(TipoChavePixManual.Email, "Nome <loja@example.test>")]
    [InlineData((TipoChavePixManual)99, "loja@example.test")]
    public void RejeitaTipoOuFormatoInvalido(TipoChavePixManual tipo, string chave) =>
        Assert.Throws<ArgumentException>(() => ConfiguracaoPixManual.Criar(tipo, chave, "Loja", null));
}
