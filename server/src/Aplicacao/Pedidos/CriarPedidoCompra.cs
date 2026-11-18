using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ItemCompraInput(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade);

public sealed record CriarPedidoCompraInput(
    Guid ClienteId, Guid ChaveIdempotencia, IReadOnlyList<ItemCompraInput> Itens);

public enum CriarPedidoCompraStatus
{
    Sucesso,
    DadosInvalidos,
    ClienteInapto,
    ItemNaoCompravel,
    LimiteMonetarioExcedido,
    SaldoInsuficiente,
    ConflitoIdempotencia,
    ConflitoConcorrencia,
}

public sealed record CriarPedidoCompraResult(
    CriarPedidoCompraStatus Status, Pedido? Pedido = null, bool Repetida = false);

public interface ICriarPedidoCompraRepository
{
    Task<CriarPedidoCompraResult> CriarAsync(
        CriarPedidoCompraInput normalizado, CancellationToken cancellationToken);
}

public sealed class CriarPedidoCompra(ICriarPedidoCompraRepository repository)
{
    public Task<CriarPedidoCompraResult> ExecuteAsync(
        CriarPedidoCompraInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.ClienteId == Guid.Empty || input.ChaveIdempotencia == Guid.Empty
            || input.Itens is not { Count: > 0 })
        {
            return DadosInvalidos();
        }

        var porVariante = new Dictionary<Guid, ItemCompraInput>();
        foreach (var item in input.Itens)
        {
            if (item is null || item.ProdutoId == Guid.Empty || item.ProdutoVarianteId == Guid.Empty
                || item.Quantidade <= 0)
            {
                return DadosInvalidos();
            }

            if (porVariante.TryGetValue(item.ProdutoVarianteId, out var anterior))
            {
                if (anterior.ProdutoId != item.ProdutoId || anterior.Quantidade > int.MaxValue - item.Quantidade)
                {
                    return DadosInvalidos();
                }

                porVariante[item.ProdutoVarianteId] = item with { Quantidade = anterior.Quantidade + item.Quantidade };
            }
            else
            {
                porVariante.Add(item.ProdutoVarianteId, item);
            }
        }

        return repository.CriarAsync(input with
        {
            Itens = porVariante.Values.OrderBy(item => item.ProdutoVarianteId).ToArray(),
        }, cancellationToken);
    }

    private static Task<CriarPedidoCompraResult> DadosInvalidos() =>
        Task.FromResult(new CriarPedidoCompraResult(CriarPedidoCompraStatus.DadosInvalidos));
}
