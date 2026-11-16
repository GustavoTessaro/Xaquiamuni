namespace Xaquiamuni.Dominio.Estoque;

public sealed class SaldoInsumo
{
    private SaldoInsumo(
        Guid id,
        Guid insumoId,
        Guid? loteId,
        QuantidadeEstoque quantidadeFisica)
    {
        Id = id;
        InsumoId = insumoId;
        LoteId = loteId;
        QuantidadeFisica = quantidadeFisica;
    }

    private SaldoInsumo()
    {
    }

    public Guid Id { get; private set; }
    public Guid InsumoId { get; private set; }
    public Guid? LoteId { get; private set; }
    public QuantidadeEstoque QuantidadeFisica { get; private set; } = null!;

    public static SaldoInsumo CriarZero(InsumoEstoque insumo, LoteInsumo? lote = null)
    {
        ArgumentNullException.ThrowIfNull(insumo);

        if (insumo.ControlaLote)
        {
            if (lote is null)
            {
                throw new ArgumentException(
                    "O saldo de um insumo controlado por lote deve identificar o lote.",
                    nameof(lote));
            }

            if (lote.InsumoId != insumo.Id)
            {
                throw new ArgumentException(
                    "O lote não pertence ao insumo informado.",
                    nameof(lote));
            }
        }
        else if (lote is not null)
        {
            throw new ArgumentException(
                "Um insumo sem controle por lote deve usar saldo agregado.",
                nameof(lote));
        }

        return new SaldoInsumo(
            Guid.NewGuid(),
            insumo.Id,
            lote?.Id,
            QuantidadeEstoque.Criar(0m, insumo.UnidadeBase));
    }
}
