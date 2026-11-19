using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class OrdemProducaoTests
{
    [Fact]
    public void PreservaNecessidadesDaVersaoOriginalEAuditaCancelamentoUmaVez()
    {
        var autor = Guid.NewGuid();
        var agora = DateTimeOffset.UnixEpoch;
        var variante = ProdutoVariante.Criar("Lavanda", "UN", 10, true, false);
        var material = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var item = ItemReceitaProducao.Criar(material, 7m, material.UnidadeBase);
        var receita = ReceitaProducao.Criar(variante, "Receita", 2, [item], autor, agora);
        var ordem = OrdemProducao.Criar(receita, 4, autor, agora);
        receita.Editar("Receita", 4, [item], autor, agora);
        Assert.NotEqual(receita.Atual.Id, ordem.ReceitaVersaoId);
        Assert.Equal(14m, Assert.Single(ordem.Necessidades).Quantidade);
        var chave = Guid.NewGuid();
        ordem.Cancelar(autor, agora.AddHours(1), chave);
        ordem.Cancelar(Guid.NewGuid(), agora.AddHours(2), Guid.NewGuid());
        Assert.Equal(SituacaoOrdemProducao.Cancelada, ordem.Situacao);
        Assert.Equal(chave, ordem.ChaveCancelamento);
        Assert.Equal(autor, ordem.EncerradaPorId);
        Assert.Equal(agora.AddHours(1), ordem.EncerradaEmUtc);
    }
}
