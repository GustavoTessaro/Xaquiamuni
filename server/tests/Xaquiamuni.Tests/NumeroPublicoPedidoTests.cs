using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class NumeroPublicoPedidoTests
{
    [Fact]
    public void GeraCodigoComFormatoEApenasCaracteresPermitidos()
    {
        for (var i = 0; i < 64; i++)
        {
            var codigo = NumeroPublicoPedido.Gerar();
            Assert.Matches("^XQ-[23456789ACDEFHJKMNPQRTUVWXY]{10}$", codigo);
            Assert.Equal(codigo, NumeroPublicoPedido.Validar(codigo));
        }
    }

    [Theory]
    [InlineData("XQ-0123456789")]
    [InlineData("XQ-IO23456789")]
    [InlineData("XQ-abcdef2345")]
    [InlineData("XQ-23456789A ")]
    [InlineData("XQ-23456789")]
    public void RejeitaCodigoForaDoFormato(string codigo) =>
        Assert.Throws<ArgumentException>(() => NumeroPublicoPedido.Validar(codigo));
}
