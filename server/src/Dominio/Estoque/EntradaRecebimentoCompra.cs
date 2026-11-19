namespace Xaquiamuni.Dominio.Estoque;

public sealed class EntradaRecebimentoCompra
{
    public Guid ComponenteId { get; private set; }
    public Guid CompraId { get; private set; }
    public Guid LinhaId { get; private set; }
    public Guid MovimentoId { get; private set; }
    public int Ordem { get; private set; }

    private EntradaRecebimentoCompra() { }

    public EntradaRecebimentoCompra(CompraMateriaPrima compra, LinhaCompraMateriaPrima linha,
        ComponenteCompraMateriaPrima componente, MovimentoInsumo movimento, int ordem)
    {
        if (movimento.Tipo != TipoMovimentoInsumo.Aquisicao || movimento.InsumoId != componente.InsumoId)
            throw new ArgumentException("O movimento não corresponde ao componente da compra.");

        CompraId = compra.Id;
        LinhaId = linha.Id;
        ComponenteId = componente.Id;
        MovimentoId = movimento.Id;
        Ordem = ordem;
    }
}
