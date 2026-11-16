namespace Xaquiamuni.Dominio.Estoque;

public sealed class ResultadoConversaoCompraInsumo
{
    internal ResultadoConversaoCompraInsumo(
        Guid insumoId,
        Guid formaCompraId,
        string nomeFormaCompra,
        decimal quantidadeEmbalagens,
        QuantidadeEstoque fatorAplicado,
        QuantidadeEstoque quantidadeBase)
    {
        InsumoId = insumoId;
        FormaCompraId = formaCompraId;
        NomeFormaCompra = nomeFormaCompra;
        QuantidadeEmbalagens = quantidadeEmbalagens;
        FatorAplicado = fatorAplicado;
        QuantidadeBase = quantidadeBase;
    }

    public Guid InsumoId { get; }
    public Guid FormaCompraId { get; }
    public string NomeFormaCompra { get; }
    public decimal QuantidadeEmbalagens { get; }
    public QuantidadeEstoque FatorAplicado { get; }
    public QuantidadeEstoque QuantidadeBase { get; }
}
