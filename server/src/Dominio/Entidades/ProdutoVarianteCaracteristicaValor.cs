namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoVarianteCaracteristicaValor
{
    private ProdutoVarianteCaracteristicaValor()
    {
    }

    private ProdutoVarianteCaracteristicaValor(
        Guid produtoId,
        ProdutoVariante variante,
        ProdutoCaracteristicaValor valor)
    {
        ProdutoId = produtoId;
        ProdutoVarianteId = variante.Id;
        ProdutoCaracteristicaId = valor.ProdutoCaracteristicaId;
        ProdutoCaracteristicaValorId = valor.Id;
        Variante = variante;
        Caracteristica = valor.Caracteristica;
        Valor = valor;
    }

    public Guid ProdutoId { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public Guid ProdutoCaracteristicaId { get; private set; }
    public Guid ProdutoCaracteristicaValorId { get; private set; }
    public ProdutoVariante Variante { get; private set; } = null!;
    public ProdutoCaracteristica Caracteristica { get; private set; } = null!;
    public ProdutoCaracteristicaValor Valor { get; private set; } = null!;

    internal static ProdutoVarianteCaracteristicaValor Criar(
        ProdutoVariante variante,
        ProdutoCaracteristicaValor valor) =>
        new(variante.ProdutoId, variante, valor);
}
