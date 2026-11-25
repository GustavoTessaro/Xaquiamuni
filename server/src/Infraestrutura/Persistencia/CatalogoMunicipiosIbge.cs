using System.Text.Json;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public static class CatalogoMunicipiosIbge
{
    public sealed record Municipio(string CodigoIbge, string Nome);
    public sealed record Estado(string Uf, string Nome, IReadOnlyList<Municipio> Municipios);
    private sealed record Arquivo(IReadOnlyList<Estado> Estados);

    public static IReadOnlyList<Estado> Estados { get; } = Carregar();
    private static readonly Dictionary<string, (string Uf, Municipio Municipio)> PorCodigo = Estados
        .SelectMany(estado => estado.Municipios.Select(municipio => (estado.Uf, Municipio: municipio)))
        .ToDictionary(item => item.Municipio.CodigoIbge, StringComparer.Ordinal);

    internal static Municipio? Resolver(string codigo, string? uf, string nome) =>
        PorCodigo.TryGetValue(codigo, out var item) && item.Uf == uf &&
        Aplicacao.Pedidos.ResultadoCep.MesmoMunicipio(item.Municipio.Nome, nome) ? item.Municipio : null;

    private static IReadOnlyList<Estado> Carregar()
    {
        using var arquivo = typeof(CatalogoMunicipiosIbge).Assembly
            .GetManifestResourceStream("MunicipiosIbge.json")
            ?? throw new InvalidOperationException("Catálogo municipal do IBGE ausente.");
        return JsonSerializer.Deserialize<Arquivo>(arquivo, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Estados;
    }
}
