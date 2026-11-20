using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class VendaExternaTests
{
    [Fact]
    public void CalculaCentavosEProtegeRecebimentoECancelamento()
    {
        var variante = ProdutoVariante.Criar("Unidade", "UN", 8m, padrao: true);
        Produto.Criar("Sabonete", null, Categoria.Criar("Artesanais", null, 0), [variante]);
        var dia = new DateOnly(2026, 11, 20);
        var agora = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var autor = Guid.NewGuid();
        var venda = VendaExterna.Criar(CanalVendaExterna.Feira, null, dia, "Referência", dia.AddDays(7),
            [new(variante, 3, 899, 99)], autor, agora);
        Assert.Equal(2598, venda.TotalCentavos);
        Assert.Equal("Pendente", venda.SituacaoPagamento);
        venda.Receber(998, dia, MeioRecebimentoVendaExterna.Pix, autor, Guid.NewGuid(), agora);
        Assert.Equal("Parcial", venda.SituacaoPagamento);
        Assert.Equal(1600, venda.SaldoCentavos);
        Assert.Throws<InvalidOperationException>(() => venda.Receber(1601, dia,
            MeioRecebimentoVendaExterna.Dinheiro, autor, Guid.NewGuid(), agora));
        Assert.Throws<InvalidOperationException>(() => venda.Cancelar("Engano", autor, agora));
        venda.Receber(1600, dia, MeioRecebimentoVendaExterna.Dinheiro, autor, Guid.NewGuid(), agora);
        Assert.Equal("Paga", venda.SituacaoPagamento);
        Assert.Throws<OverflowException>(() => new ItemVendaExterna(variante, 2, long.MaxValue, 0));
        Assert.Throws<ArgumentException>(() => new ItemVendaExterna(variante, 1, 1, 2));
    }
}
