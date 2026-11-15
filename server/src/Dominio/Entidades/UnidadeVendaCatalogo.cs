namespace Xaquiamuni.Dominio.Entidades;

public static class UnidadeVendaCatalogo
{
    public const int CodigoMaxLength = 10;

    private static readonly HashSet<string> CodigosPermitidos =
    [
        "UN",
        "KIT",
        "PAR",
        "PCT",
        "CX",
        "CJ"
    ];

    public static string Normalizar(string? unidadeVenda)
    {
        if (string.IsNullOrWhiteSpace(unidadeVenda))
        {
            throw new ArgumentException("A unidade de venda é obrigatória.", nameof(unidadeVenda));
        }

        var normalized = unidadeVenda.Trim().ToUpperInvariant();
        if (!CodigosPermitidos.Contains(normalized))
        {
            throw new ArgumentException(
                "A unidade de venda deve ser UN, KIT, PAR, PCT, CX ou CJ.",
                nameof(unidadeVenda));
        }

        return normalized;
    }
}
