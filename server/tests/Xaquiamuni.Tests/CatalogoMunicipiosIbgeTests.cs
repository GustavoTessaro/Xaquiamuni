using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public sealed class CatalogoMunicipiosIbgeTests
{
    [Fact]
    public void CatalogoEmbutidoTemTodosOsEstadosECodigosUnicos()
    {
        var estados = CatalogoMunicipiosIbge.Estados;
        Assert.Equal(27, estados.Count);
        Assert.All(estados, estado =>
        {
            Assert.Matches("^[A-Z]{2}$", estado.Uf);
            Assert.NotEmpty(estado.Nome);
            Assert.NotEmpty(estado.Municipios);
        });
        var municipios = estados.SelectMany(estado => estado.Municipios).ToArray();
        Assert.Equal(5571, municipios.Length);
        Assert.Equal(municipios.Length, municipios.Select(item => item.CodigoIbge).Distinct().Count());
        Assert.All(municipios, item =>
        {
            Assert.Matches("^[0-9]{7}$", item.CodigoIbge);
            Assert.NotEmpty(item.Nome);
        });
    }

    [Theory]
    [InlineData("4322509", "RS", "Vacaria", true)]
    [InlineData("4209300", "SC", "Lages", true)]
    [InlineData("3550308", "SP", "Sao Paulo", true)]
    [InlineData("4322509", "RS", "Porto Alegre", false)]
    [InlineData("4322509", "SC", "Vacaria", false)]
    [InlineData("4300000", "RS", "Vacaria", false)]
    public void ValidaIdentidadeMunicipal(string codigo, string uf, string nome, bool esperado) =>
        Assert.Equal(esperado, CatalogoMunicipiosIbge.Resolver(codigo, uf, nome) is not null);
}
