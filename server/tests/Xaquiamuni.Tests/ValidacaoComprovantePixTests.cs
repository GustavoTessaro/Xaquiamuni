using System.Text;
using SkiaSharp;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Tests.Integration;

namespace Xaquiamuni.Tests;

public sealed class ValidacaoComprovantePixTests
{
    [Theory]
    [InlineData("recibo.jpg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("recibo.JPEG", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("recibo.png", "image/png", SKEncodedImageFormat.Png)]
    public void ValidaERecusaImagemTruncada(string nome, string mime, SKEncodedImageFormat formato)
    {
        var bytes = CadeiaFabricacaoSqlServerIntegrationTests.ImagemComprovante(formato);
        Assert.Equal(mime, ValidacaoComprovantePix.TipoValidado(nome, mime, bytes));
        Assert.Null(ValidacaoComprovantePix.TipoValidado(nome, mime, bytes[..^2]));
        Assert.Null(ValidacaoComprovantePix.TipoValidado(nome, "application/pdf", bytes));
    }

    [Fact]
    public void ValidaPdfClassicoERejeitaOffsetInexistente()
    {
        var bytes = CadeiaFabricacaoSqlServerIntegrationTests.PdfComprovante();
        Assert.Equal("application/pdf", ValidacaoComprovantePix.TipoValidado("recibo.pdf", "application/pdf", bytes));
        var texto = Encoding.ASCII.GetString(bytes).Replace("0000000009 00000 n", "9999999999 00000 n");
        Assert.NotEqual(Encoding.ASCII.GetString(bytes), texto);
        Assert.Null(ValidacaoComprovantePix.TipoValidado("recibo.pdf", "application/pdf", Encoding.ASCII.GetBytes(texto)));
    }

    [Theory]
    [InlineData("/OpenAction << /S /JavaScript >>")]
    [InlineData("/Launch")]
    [InlineData("/Java#53cript")]
    [InlineData("/EmbeddedFiles")]
    public void RejeitaRecursosAtivosEmPdf(string extra) =>
        Assert.Null(ValidacaoComprovantePix.TipoValidado("recibo.pdf", "application/pdf",
            CadeiaFabricacaoSqlServerIntegrationTests.PdfComprovante(extra)));
}
