namespace Xaquiamuni.Dominio.Entidades;

// Os dados comerciais devem ser obtidos pelo caso de uso a partir do catálogo.
public sealed record DadosItemPedido(
    Guid ProdutoId,
    Guid ProdutoVarianteId,
    string NomeProduto,
    string NomeVariante,
    int Quantidade,
    long PrecoUnitarioCentavos);

public sealed class ItemPedido
{
    private ItemPedido()
    {
    }

    public Guid Id { get; private set; }
    public Guid PedidoId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public string NomeProduto { get; private set; } = null!;
    public string NomeVariante { get; private set; } = null!;
    public int Quantidade { get; private set; }
    public long PrecoUnitarioCentavos { get; private set; }
    public long SubtotalCentavos => checked(PrecoUnitarioCentavos * Quantidade);

    internal static ItemPedido Criar(Guid pedidoId, DadosItemPedido dados)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentOutOfRangeException.ThrowIfEqual(dados.ProdutoId, Guid.Empty, nameof(dados.ProdutoId));
        ArgumentOutOfRangeException.ThrowIfEqual(dados.ProdutoVarianteId, Guid.Empty, nameof(dados.ProdutoVarianteId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dados.Quantidade, nameof(dados.Quantidade));
        ArgumentOutOfRangeException.ThrowIfNegative(dados.PrecoUnitarioCentavos, nameof(dados.PrecoUnitarioCentavos));

        return new ItemPedido
        {
            Id = Guid.NewGuid(),
            PedidoId = pedidoId,
            ProdutoId = dados.ProdutoId,
            ProdutoVarianteId = dados.ProdutoVarianteId,
            NomeProduto = NormalizarNome(dados.NomeProduto, Produto.NomeMaxLength, nameof(dados.NomeProduto)),
            NomeVariante = NormalizarNome(dados.NomeVariante, ProdutoVariante.IdentificacaoMaxLength, nameof(dados.NomeVariante)),
            Quantidade = dados.Quantidade,
            PrecoUnitarioCentavos = dados.PrecoUnitarioCentavos,
        };
    }

    private static string NormalizarNome(string nome, int limite, string parametro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome, parametro);
        var normalizado = nome.Trim();
        if (normalizado.Length > limite)
        {
            throw new ArgumentException($"O nome não pode exceder {limite} caracteres.", parametro);
        }
        return normalizado;
    }
}
