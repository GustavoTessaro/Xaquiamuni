using System.Globalization;

namespace Xaquiamuni.Dominio.Entidades;

public static class MedidaFisicaCatalogo
{
    public const int Precision = 12;
    public const int Scale = 3;
    public const decimal MaximoCanonico = 999999999.999m;

    public static decimal ConverterPesoParaGramas(decimal quantidade, string? unidade)
    {
        var normalizedUnit = NormalizarUnidade(unidade, nameof(unidade));
        var multiplicador = normalizedUnit switch
        {
            "G" => 1m,
            "KG" => 1000m,
            _ => throw new ArgumentException("A unidade de peso deve ser G ou KG.", nameof(unidade))
        };

        return ConverterParaCanonico(quantidade, multiplicador, nameof(quantidade));
    }

    public static decimal ConverterVolumeParaMililitros(decimal quantidade, string? unidade)
    {
        var normalizedUnit = NormalizarUnidade(unidade, nameof(unidade));
        var multiplicador = normalizedUnit switch
        {
            "ML" => 1m,
            "L" => 1000m,
            _ => throw new ArgumentException("A unidade de volume deve ser ML ou L.", nameof(unidade))
        };

        return ConverterParaCanonico(quantidade, multiplicador, nameof(quantidade));
    }

    public static string FormatarValor(TipoMedidaFisica tipo, decimal valorCanonico) =>
        tipo switch
        {
            TipoMedidaFisica.PesoLiquido =>
                $"{valorCanonico.ToString("0.###", CultureInfo.InvariantCulture)} g",
            TipoMedidaFisica.VolumeLiquido =>
                $"{valorCanonico.ToString("0.###", CultureInfo.InvariantCulture)} ml",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };

    public static decimal? ValidarPesoCanonico(decimal? gramas, string paramName = "pesoLiquidoGramas") =>
        ValidarCanonico(gramas, paramName);

    public static decimal? ValidarVolumeCanonico(
        decimal? mililitros,
        string paramName = "volumeLiquidoMililitros") =>
        ValidarCanonico(mililitros, paramName);

    private static decimal ConverterParaCanonico(decimal quantidade, decimal multiplicador, string paramName)
    {
        if (quantidade <= 0 || quantidade > MaximoCanonico / multiplicador)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "A medida física deve ser positiva e caber na precisão suportada.");
        }

        return ValidarCanonico(quantidade * multiplicador, paramName)!.Value;
    }

    private static decimal? ValidarCanonico(decimal? valor, string paramName)
    {
        if (valor is null)
        {
            return null;
        }

        if (valor <= 0 || valor > MaximoCanonico || decimal.Round(valor.Value, Scale) != valor.Value)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                $"A medida deve ser positiva, ter até {Scale} casas decimais e caber em decimal({Precision},{Scale}).");
        }

        return valor;
    }

    private static string NormalizarUnidade(string? unidade, string paramName)
    {
        if (string.IsNullOrWhiteSpace(unidade))
        {
            throw new ArgumentException("A unidade física é obrigatória.", paramName);
        }

        return unidade.Trim().ToUpperInvariant();
    }
}
