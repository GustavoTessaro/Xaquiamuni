namespace Xaquiamuni.Aplicacao.Pedidos;

public interface IConsultaCep
{
    Task<ResultadoCep> ConsultarAsync(string? cep, CancellationToken ct);
}

public sealed record EnderecoCep(string Cep, string? Uf, string? Cidade, string CodigoIbge,
    string? Bairro = null, string? Logradouro = null);

public sealed record ResultadoCep(EnderecoCep? Endereco, string? Codigo = null)
{
    public static string? Normalizar(string? cep)
    {
        var valor = cep?.Trim();
        if (valor is { Length: 9 } && valor[5] == '-')
            valor = valor.Remove(5, 1);
        return valor is { Length: 8 } && valor.All(char.IsAsciiDigit) ? valor : null;
    }

    public static bool MesmoMunicipio(string? a, string? b) =>
        System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(a, b,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) == 0;
}
