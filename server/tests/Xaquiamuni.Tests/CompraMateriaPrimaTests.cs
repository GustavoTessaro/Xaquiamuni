using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class CompraMateriaPrimaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConverteApresentacaoECalculaTotalSemRatearKit()
    {
        var extrato = InsumoEstoque.Criar("Extrato", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
        var frasco = extrato.AdicionarFormaCompra("Frasco 100 ml", 100, UnidadeFisicaEstoque.Mililitro);
        var componente = ComponenteCompraMateriaPrima.Criar(0, extrato, frasco.Id, 2);
        Assert.Equal(200m, componente.QuantidadeBase);
        Assert.Equal(100m, componente.Fator);
        Assert.Equal("Frasco 100 ml", componente.NomeApresentacao);
        var simples = LinhaCompraMateriaPrima.Criar(0, null, 2, 0, [componente]);
        var nomes = new[] { "Arruda Macho", "Arruda Fêmea", "Alecrim", "Alevante",
            "Espada de São Jorge", "Guiné", "Comigo-ninguém-pode" };
        var ervas = nomes.Select((nome, i) => ComponenteCompraMateriaPrima.Criar(i,
            InsumoEstoque.Criar(nome, TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama),
            null, i + 0.5m)).ToArray();
        var kit = LinhaCompraMateriaPrima.Criar(1, "Sete ervas", 1, 27.49m, ervas);
        var fornecedor = Fornecedor.Criar("Vale", null, null, null, Agora);
        var admin = Guid.NewGuid();
        var compra = CompraMateriaPrima.Criar(fornecedor, new(2026, 11, 19), [simples, kit], admin, Guid.NewGuid(), "hash", Agora);
        Assert.Equal(27.49m, compra.Total);
        Assert.Equal(7, compra.Linhas[1].Componentes.Count);
        Assert.Equal(6.5m, compra.Linhas[1].Componentes[6].QuantidadeBase);
        Assert.Equal(0, compra.Linhas[0].ValorTotal);

        var desconhecida = LinhaCompraMateriaPrima.Criar(0, null, 2, null, [componente.CopiarSnapshot(0)]);
        compra.Editar(fornecedor, compra.DataCompra, [desconhecida, kit], admin, Agora);
        Assert.Null(compra.Total);
        compra.Cancelar(admin, Agora);
        var versao = compra.Versao;
        compra.Cancelar(admin, Agora.AddHours(1));
        Assert.Equal(versao, compra.Versao);
        Assert.Throws<InvalidOperationException>(() => compra.Editar(fornecedor, compra.DataCompra, [kit], admin, Agora));
    }

    [Fact]
    public void RecebimentoRegistraAuditoriaEEncerraRascunho()
    {
        var fornecedor = Fornecedor.Criar("Vale", null, null, null, Agora);
        var insumo = InsumoEstoque.Criar("Ervas", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var linha = LinhaCompraMateriaPrima.Criar(0, null, 2, null,
            [ComponenteCompraMateriaPrima.Criar(0, insumo, null, 2)]);
        var criador = Guid.NewGuid();
        var recebedor = Guid.NewGuid();
        var chave = Guid.NewGuid();
        var compra = CompraMateriaPrima.Criar(fornecedor, new(2026, 11, 19), [linha], criador, Guid.NewGuid(), "criação", Agora);
        var versao = compra.Versao;
        var horario = Agora.AddHours(2).ToOffset(TimeSpan.FromHours(-3));
        compra.ConfirmarRecebimento(recebedor, chave, "recebimento", horario);
        Assert.Equal(SituacaoCompraMateriaPrima.Recebida, compra.Situacao);
        Assert.Equal(recebedor, compra.RecebidaPorId);
        Assert.Equal(horario.ToUniversalTime(), compra.RecebidaEmUtc);
        Assert.Equal(TimeSpan.Zero, compra.RecebidaEmUtc!.Value.Offset);
        Assert.Equal(chave, compra.ChaveRecebimento);
        Assert.Equal(criador, compra.CriadoPorId);
        Assert.Equal("Vale", compra.NomeFornecedor);
        Assert.NotEqual(versao, compra.Versao);
        Assert.Null(compra.Total);
        Assert.Throws<InvalidOperationException>(() => compra.ConfirmarRecebimento(recebedor, chave, "recebimento", horario));
        Assert.Throws<InvalidOperationException>(() => compra.Cancelar(recebedor, horario));

        var cancelada = CompraMateriaPrima.Criar(fornecedor, compra.DataCompra, [linha], criador, Guid.NewGuid(), "criação", Agora);
        cancelada.Cancelar(criador, horario);
        Assert.Throws<InvalidOperationException>(() => cancelada.ConfirmarRecebimento(recebedor, chave, "recebimento", horario));
    }

    [Fact]
    public void RejeitaInativosQuantidadesInvalidasEArredondamentoSilencioso()
    {
        var material = InsumoEstoque.Criar("Frascos", TipoInsumoEstoque.Embalagem, UnidadeFisicaEstoque.Unidade);
        Assert.ThrowsAny<ArgumentException>(() => ComponenteCompraMateriaPrima.Criar(0, material, null, 1.5m));
        Assert.ThrowsAny<ArgumentException>(() => ComponenteCompraMateriaPrima.Criar(0, material, null, 0m));
        var item = ComponenteCompraMateriaPrima.Criar(0, material, null, 1);
        Assert.Throws<ArgumentException>(() => LinhaCompraMateriaPrima.Criar(0, null, 1, 0.001m, [item]));
        Assert.Throws<ArgumentException>(() => LinhaCompraMateriaPrima.Criar(0, "Kit", 1, 1m, [item]));
        material.DefinirAtivo(false);
        Assert.Throws<ArgumentException>(() => ComponenteCompraMateriaPrima.Criar(0, material, null, 1));
        var fornecedor = Fornecedor.Criar("Vale", null, null, null, Agora);
        fornecedor.DefinirAtivo(false, Agora);
        var linha = LinhaCompraMateriaPrima.Criar(0, null, 1, 1, [item]);
        Assert.Throws<ArgumentException>(() =>
            CompraMateriaPrima.Criar(fornecedor, new(2026, 11, 19), [linha], Guid.NewGuid(), Guid.NewGuid(), "hash", Agora));
    }
}
