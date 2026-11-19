using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class ReceitaProducaoTests
{
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = DateTimeOffset.UnixEpoch;

    [Fact]
    public void PreservaVersaoECalculaNecessidadesExatasNasTresUnidades()
    {
        var itens = new[] { Item("Base", 100.000002m, UnidadeFisicaEstoque.Grama),
            Item("Essência", 7.5m, UnidadeFisicaEstoque.Mililitro), Item("Embalagem", 2, UnidadeFisicaEstoque.Unidade) };
        var receita = ReceitaProducao.Criar(Variante(), "Receita", 2, itens, Autor, Agora);
        var primeira = receita.Atual;
        Assert.Equal(1, primeira.Numero);
        var necessidades = primeira.CalcularNecessidades(3);
        Assert.Equal([150.000003m, 11.25m, 3m], necessidades.Select(n => n.Quantidade.ValorBase));
        receita.Editar("Outro nome", 2, itens.Reverse().ToArray(), Autor, Agora);
        Assert.Same(primeira, receita.Atual);
        receita.Editar("Outro nome", 3, itens, Autor, Agora);
        Assert.Equal(2, receita.Atual.Numero);
        Assert.Equal(2, primeira.Rendimento);
        Assert.Equal(100.000002m, primeira.Itens[0].Quantidade);
        Assert.Throws<ArgumentException>(() => primeira.CalcularNecessidades(0));
    }

    [Theory]
    [InlineData(UnidadeFisicaEstoque.Grama)]
    [InlineData(UnidadeFisicaEstoque.Mililitro)]
    [InlineData(UnidadeFisicaEstoque.Unidade)]
    public void RejeitaDivisaoInexata(UnidadeFisicaEstoque unidade)
    {
        var receita = ReceitaProducao.Criar(Variante(), "Receita", 3, [Item("Material", 1, unidade)], Autor, Agora);
        Assert.Throws<ArgumentException>(() => receita.Atual.CalcularNecessidades(1));
        Assert.Equal(2m, Assert.Single(receita.Atual.CalcularNecessidades(6)).Quantidade.ValorBase);
    }

    [Fact]
    public void RejeitaDadosInvalidos()
    {
        var material = InsumoEstoque.Criar("Material", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        Assert.ThrowsAny<ArgumentException>(() => ItemReceitaProducao.Criar(material, -1, material.UnidadeBase));
        Assert.ThrowsAny<ArgumentException>(() => ItemReceitaProducao.Criar(material, 0.0000001m, material.UnidadeBase));
        Assert.Throws<ArgumentException>(() => ItemReceitaProducao.Criar(material, 1, UnidadeFisicaEstoque.Mililitro));
        Assert.ThrowsAny<ArgumentException>(() => Item("Unidade", 1.5m, UnidadeFisicaEstoque.Unidade));
        var item = ItemReceitaProducao.Criar(material, 1, material.UnidadeBase);
        Assert.Throws<ArgumentException>(() => ReceitaProducao.Criar(Variante(), "Receita", 0, [item], Autor, Agora));
        Assert.Throws<ArgumentException>(() => ReceitaProducao.Criar(Variante(), "Receita", 1, [item, item], Autor, Agora));
        Assert.Throws<ArgumentException>(() => ReceitaProducao.Criar(Variante(false), "Receita", 1, [item], Autor, Agora));
        material.DefinirAtivo(false);
        Assert.Throws<ArgumentException>(() => ItemReceitaProducao.Criar(material, 1, material.UnidadeBase));
    }

    private static ProdutoVariante Variante(bool ativa = true) => ProdutoVariante.Criar("Lavanda", "UN", 10, ativa, false);
    private static ItemReceitaProducao Item(string nome, decimal quantidade, UnidadeFisicaEstoque unidade) =>
        ItemReceitaProducao.Criar(InsumoEstoque.Criar(nome, TipoInsumoEstoque.MateriaPrima, unidade), quantidade, unidade);
}
