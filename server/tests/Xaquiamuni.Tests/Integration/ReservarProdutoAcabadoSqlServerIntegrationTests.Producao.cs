using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ConclusaoProducao_CompatibilidadeCatalogoCheckoutESaidasConcorrentes()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var seed = banco.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(seed);
        var (produto, variante, _) = await CriarProdutoCompravelAsync(seed);
        var calendario = new CalendarioOperacionalEstoque(TimeProvider.System, Options.Create(new CalendarioOperacionalEstoqueOptions()));
        var data = calendario.ObterDataAtual();
        var catalogo = new EfCatalogoPublicoRepository(seed, calendario);
        Assert.DoesNotContain(variante.Id, (await catalogo.ConsultarProdutoAsync(produto.Id))!.OpcoesDisponiveis);

        var insumo = InsumoEstoque.Criar("Base de produção", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var receita = ReceitaProducao.Criar(variante, "Receita de produção", 2,
            [ItemReceitaProducao.Criar(insumo, 5, insumo.UnidadeBase)], Responsavel, DateTimeOffset.UtcNow);
        // A variante já existe. A referência é carregada antes de anexar a receita.
        seed.Attach(variante);
        seed.InsumosEstoque.Add(insumo);
        seed.ReceitasProducao.Add(receita);
        await seed.SaveChangesAsync();
        var estoque = new EfEstoqueInsumoRepository(seed, TimeProvider.System, calendario);
        var entradaInsumo = await estoque.RegistrarEntradaAsync(new(insumo.Id, Responsavel, TipoMovimentoInsumo.EstoqueInicial,
            5, insumo.UnidadeBase, null, null, null, null, null, null, false, null, Guid.NewGuid().ToString("N")));
        Assert.True(entradaInsumo.Sucesso, entradaInsumo.Erro);
        var inicio = await estoque.IniciarAsync(new(receita.Id, receita.Atual.Id, 2), Responsavel, Guid.NewGuid(), default);
        Assert.Equal(OperacaoProducaoStatus.Sucesso, inicio.Status);
        var ordem = await seed.OrdensProducao.AsNoTracking().SingleAsync();
        var input = new ConcluirProducaoInput(ordem.Versao, 2, [new(insumo.Id, "Grama", "5")], null, data.AddDays(5));

        await using var ctxProducao = banco.CreateVerificationContext();
        await using var ctxManual = banco.CreateVerificationContext();
        await using var ctxCompra = banco.CreateVerificationContext();
        await using var ctxSaida = banco.CreateVerificationContext();
        var producao = new EfEstoqueInsumoRepository(ctxProducao, TimeProvider.System, calendario);
        var saida = new EfEstoqueInsumoRepository(ctxSaida, TimeProvider.System, calendario);
        var pedido = new CriarPedidoCompraInput(cliente, Guid.NewGuid(), [new(produto.Id, variante.Id, 2)]);
        var concluindo = producao.ConcluirAsync(ordem.Id, Responsavel, Guid.NewGuid(), input, default);
        var lancando = EntrarAsync(ctxManual, variante.Id, 1, validade: data.AddDays(20));
        var comprando = Compra(ctxCompra).ExecuteAsync(pedido);
        var saindo = saida.RegistrarSaidaAsync(new(insumo.Id, Responsavel, TipoMovimentoInsumo.SaidaManual, 1,
            insumo.UnidadeBase, null, "Teste de concorrência", Guid.NewGuid().ToString("N")));
        await Task.WhenAll(concluindo, lancando, comprando, saindo);
        Assert.Equal(OperacaoProducaoStatus.Sucesso, (await concluindo).Status);
        Assert.True((await lancando).Sucesso);
        Assert.Equal(OperacaoEstoqueInsumoStatus.SaldoInsuficiente, (await saindo).Status);
        var conclusao = await producao.ConsultarConclusaoAsync(ordem.Id, default);
        var comprado = await comprando;
        if (comprado.Status == CriarPedidoCompraStatus.SaldoInsuficiente)
            comprado = await Compra(ctxCompra).ExecuteAsync(pedido);
        Assert.Equal(CriarPedidoCompraStatus.Sucesso, comprado.Status);
        var reserva = await seed.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(conclusao.LoteId, reserva.LoteId);
        Assert.Equal(2, reserva.Quantidade);
        Assert.Contains(variante.Id, (await catalogo.ConsultarProdutoAsync(produto.Id))!.OpcoesDisponiveis);

        var persistido = await seed.Produtos.SingleAsync(p => p.Id == produto.Id);
        persistido.Despublicar();
        await seed.SaveChangesAsync();
        Assert.Null(await catalogo.ConsultarProdutoAsync(produto.Id));
    }
}
