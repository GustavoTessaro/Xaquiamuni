using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Aplicacao.Catalogo;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ItemCompraInput(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade,
    long? PrecoAceitoCentavos = null);
public sealed record PrecoAtualPedido(Guid ProdutoId, Guid ProdutoVarianteId, PrecoComercial Preco);

public sealed record CriarPedidoCompraInput(
    Guid ClienteId, Guid ChaveIdempotencia, IReadOnlyList<ItemCompraInput> Itens, string? OfertaEntregaId = null,
    string? ConversaoCorreiosId = null);

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
    PrecoAlterado,
    OfertaEntregaInvalida,
    CotacaoCorreiosNaoEncontrada,
    ConversaoCorreiosInvalida,
    CotacaoCorreiosExpirada,
    CotacaoCorreiosNaoAceita,
    CotacaoCorreiosConvertida,
}

public sealed record CriarPedidoCompraResult(
    CriarPedidoCompraStatus Status, Pedido? Pedido = null, bool Repetida = false,
    IReadOnlyList<PrecoAtualPedido>? PrecosAtuais = null, RevisaoConversaoCorreios? RevisaoPostal = null,
    IReadOnlyList<Guid>? ItensIndisponiveis = null);

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
        if (input.ClienteId == Guid.Empty || input.ChaveIdempotencia == Guid.Empty)
        {
            return DadosInvalidos();
        }
        if (input.ConversaoCorreiosId is not null)
            return input.Itens is { Count: 0 } && input.OfertaEntregaId is null
                && input.ConversaoCorreiosId.Length is > 0 and <= 16000
                ? repository.CriarAsync(input, cancellationToken) : DadosInvalidos();
        if (input.Itens is not { Count: > 0 }) return DadosInvalidos();

        var itens = NormalizarItens(input.Itens);
        return itens is null ? DadosInvalidos()
            : repository.CriarAsync(input with { Itens = itens }, cancellationToken);
    }

    public static ItemCompraInput[]? NormalizarItens(IReadOnlyList<ItemCompraInput> itens)
    {
        var porVariante = new Dictionary<Guid, ItemCompraInput>();
        foreach (var item in itens)
        {
            if (item is null || item.ProdutoId == Guid.Empty || item.ProdutoVarianteId == Guid.Empty
                || item.Quantidade <= 0 || item.PrecoAceitoCentavos is <= 0)
            {
                return null;
            }

            if (porVariante.TryGetValue(item.ProdutoVarianteId, out var anterior))
            {
                if (anterior.ProdutoId != item.ProdutoId || anterior.Quantidade > int.MaxValue - item.Quantidade
                    || anterior.PrecoAceitoCentavos != item.PrecoAceitoCentavos)
                {
                    return null;
                }

                porVariante[item.ProdutoVarianteId] = item with { Quantidade = anterior.Quantidade + item.Quantidade };
            }
            else
            {
                porVariante.Add(item.ProdutoVarianteId, item);
            }
        }

        return porVariante.Values.OrderBy(item => item.ProdutoVarianteId).ToArray();
    }

    private static Task<CriarPedidoCompraResult> DadosInvalidos() =>
        Task.FromResult(new CriarPedidoCompraResult(CriarPedidoCompraStatus.DadosInvalidos));
}
