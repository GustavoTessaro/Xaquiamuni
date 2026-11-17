namespace Xaquiamuni.Dominio.Estoque;

public sealed record SaldoProdutoAcabadoSnapshot
{
    private SaldoProdutoAcabadoSnapshot(
        int quantidadeVendavel,
        int quantidadeDanificada,
        int quantidadeQuarentena)
    {
        QuantidadeVendavel = quantidadeVendavel;
        QuantidadeDanificada = quantidadeDanificada;
        QuantidadeQuarentena = quantidadeQuarentena;
    }

    public int QuantidadeVendavel { get; }
    public int QuantidadeDanificada { get; }
    public int QuantidadeQuarentena { get; }

    public int QuantidadeFisica
    {
        get
        {
            try
            {
                return checked(checked(QuantidadeVendavel + QuantidadeDanificada) + QuantidadeQuarentena);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(QuantidadeFisica),
                    "A soma das condições excede o limite de quantidade inteira suportado.");
            }
        }
    }

    internal static SaldoProdutoAcabadoSnapshot Criar(
        int quantidadeVendavel,
        int quantidadeDanificada,
        int quantidadeQuarentena)
    {
        if (quantidadeVendavel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidadeVendavel));
        }

        if (quantidadeDanificada < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidadeDanificada));
        }

        if (quantidadeQuarentena < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantidadeQuarentena));
        }

        var snapshot = new SaldoProdutoAcabadoSnapshot(
            quantidadeVendavel,
            quantidadeDanificada,
            quantidadeQuarentena);
        _ = snapshot.QuantidadeFisica;
        return snapshot;
    }
}
