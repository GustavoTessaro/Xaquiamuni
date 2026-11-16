namespace Xaquiamuni.Dominio.Estoque;

public sealed record CustoAquisicaoInsumo
{
    public const int CustoTotalPrecision = 18;
    public const int CustoTotalScale = 2;
    public const int CustoUnitarioPrecision = 38;
    public const int CustoUnitarioScale = 14;
    public const decimal CustoTotalMaximo = 9_999_999_999_999_999.99m;

    private CustoAquisicaoInsumo(decimal? total, decimal? porUnidadeBase)
    {
        Total = total;
        PorUnidadeBase = porUnidadeBase;
    }

    public decimal? Total { get; }
    public decimal? PorUnidadeBase { get; }

    public static CustoAquisicaoInsumo Criar(decimal? total, QuantidadeEstoque quantidadeBase)
    {
        ArgumentNullException.ThrowIfNull(quantidadeBase);
        if (total is null)
        {
            return new CustoAquisicaoInsumo(null, null);
        }

        if (total < 0 || total > CustoTotalMaximo
            || decimal.Round(total.Value, CustoTotalScale, MidpointRounding.AwayFromZero) != total.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(total),
                "O custo total deve ser não negativo, caber em decimal(18,2) e ter até duas casas decimais.");
        }

        quantidadeBase.ExigirPositiva(nameof(quantidadeBase));

        var unitario = decimal.Round(
            total.Value / quantidadeBase.ValorBase,
            CustoUnitarioScale,
            MidpointRounding.AwayFromZero);

        return new CustoAquisicaoInsumo(total, unitario);
    }
}
