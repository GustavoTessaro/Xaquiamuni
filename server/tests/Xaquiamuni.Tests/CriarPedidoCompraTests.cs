using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Tests;

public sealed class CriarPedidoCompraTests
{
    [Fact]
    public async Task NormalizaVariantesRepetidasEOrdenaParaOsLocks()
    {
        var repository = new Repositorio();
        var cliente = Guid.NewGuid();
        var chave = Guid.NewGuid();
        var produto = Guid.NewGuid();
        var primeira = new ItemCompraInput(produto, Guid.Parse("00000000-0000-0000-0000-000000000001"), 2);
        var segunda = new ItemCompraInput(produto, Guid.Parse("00000000-0000-0000-0000-000000000002"), 3);

        await new CriarPedidoCompra(repository).ExecuteAsync(
            new(cliente, chave, [segunda, primeira, primeira with { Quantidade = 6 }]));

        var recebido = Assert.IsType<CriarPedidoCompraInput>(repository.Recebido);
        Assert.Equal(cliente, recebido.ClienteId);
        Assert.Equal(chave, recebido.ChaveIdempotencia);
        Assert.Equal([primeira with { Quantidade = 8 }, segunda], recebido.Itens);
    }

    [Fact]
    public async Task RejeitaDadosInvalidosAntesDaPersistencia()
    {
        var repository = new Repositorio();
        var useCase = new CriarPedidoCompra(repository);
        var item = new ItemCompraInput(Guid.NewGuid(), Guid.NewGuid(), 1);
        var input = new CriarPedidoCompraInput(Guid.NewGuid(), Guid.NewGuid(), [item]);
        CriarPedidoCompraInput[] invalidos =
        [
            input with { ClienteId = Guid.Empty },
            input with { ChaveIdempotencia = Guid.Empty },
            input with { Itens = [] },
            input with { Itens = [item with { ProdutoId = Guid.Empty }] },
            input with { Itens = [item with { ProdutoVarianteId = Guid.Empty }] },
            input with { Itens = [item with { Quantidade = 0 }] },
            input with { Itens = [item with { Quantidade = -1 }] },
            input with { Itens = [item, item with { ProdutoId = Guid.NewGuid() }] },
            input with { Itens = [item, item with { Quantidade = int.MaxValue }] },
        ];
        foreach (var invalido in invalidos)
        {
            Assert.Equal(CriarPedidoCompraStatus.DadosInvalidos, (await useCase.ExecuteAsync(invalido)).Status);
        }
        Assert.Null(repository.Recebido);
    }

    private sealed class Repositorio : ICriarPedidoCompraRepository
    {
        public CriarPedidoCompraInput? Recebido { get; private set; }

        public Task<CriarPedidoCompraResult> CriarAsync(
            CriarPedidoCompraInput normalizado, CancellationToken cancellationToken)
        {
            Recebido = normalizado;
            return Task.FromResult(new CriarPedidoCompraResult(CriarPedidoCompraStatus.Sucesso));
        }
    }
}
