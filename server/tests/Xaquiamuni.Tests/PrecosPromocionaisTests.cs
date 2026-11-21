using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class PrecosPromocionaisTests
{
    [Theory]
    [InlineData(999, TipoPromocao.Percentual, 5000, 500, 4994)]
    [InlineData(300, TipoPromocao.PrecoFixo, 200, 200, 3333)]
    [InlineData(1000, TipoPromocao.Percentual, 1250, 875, 1250)]
    [InlineData(1200, TipoPromocao.PrecoFixo, 900, 900, 2500)]
    public void CalculaPrecoEconomiaEOffEfetivo(long regular, TipoPromocao tipo, long valor, long final, int off)
    {
        var preco = Assert.IsType<PrecoPromocional>(PrecosPromocionais.Calcular(regular, tipo, valor));
        Assert.Equal(final, preco.Final);
        Assert.Equal(regular - final, preco.Economia);
        Assert.Equal(off, preco.OffPontosBase);
    }

    [Theory]
    [InlineData(1, TipoPromocao.Percentual, 1)]
    [InlineData(1, TipoPromocao.Percentual, 9999)]
    [InlineData(100, TipoPromocao.Percentual, 10000)]
    [InlineData(100, TipoPromocao.PrecoFixo, 0)]
    [InlineData(100, TipoPromocao.PrecoFixo, 100)]
    [InlineData(100, TipoPromocao.PrecoFixo, 101)]
    public void RecusaResultadoSemDescontoOuNaoPositivo(long regular, TipoPromocao tipo, long valor) =>
        Assert.Null(PrecosPromocionais.Calcular(regular, tipo, valor));

    [Fact]
    public void UsaEscopoEspecificoAindaQueMaisCaroEReavaliaComNovoPrecoRegular()
    {
        var dia = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var produto = Guid.NewGuid();
        var variante = Guid.NewGuid();
        var geral = Promocao.Criar(produto, Guid.NewGuid(), dia);
        geral.Configurar(TipoPromocao.Percentual, EscopoPromocao.TodasVariantes, 3000, null, null, []);
        geral.RegistrarAlteracao(true, geral.CriadaPorId, dia);
        var especifica = Promocao.Criar(produto, Guid.NewGuid(), dia);
        especifica.Configurar(TipoPromocao.PrecoFixo, EscopoPromocao.VariantesSelecionadas, 900,
            dia, dia.AddDays(1), [variante]);
        especifica.RegistrarAlteracao(true, especifica.CriadaPorId, dia);
        var antes = PrecosPromocionais.Resolver([geral, especifica], variante, 1000, dia.AddTicks(-1));
        var inicio = PrecosPromocionais.Resolver([geral, especifica], variante, 1000, dia);
        var fim = PrecosPromocionais.Resolver([geral, especifica], variante, 1000, dia.AddDays(1));
        var regularMenor = PrecosPromocionais.Resolver([geral, especifica], variante, 800, dia);
        var outraVariante = PrecosPromocionais.Resolver([geral, especifica], Guid.NewGuid(), 1000, dia);
        Assert.Same(geral, antes.Promocao);
        Assert.Same(especifica, inicio.Promocao);
        Assert.Equal(900, inicio.Preco!.Final);
        Assert.True(inicio.EspecificaPrevalece);
        Assert.Same(geral, fim.Promocao);
        Assert.Equal(560, regularMenor.Preco!.Final);
        Assert.Same(geral, outraVariante.Promocao);
    }
}
