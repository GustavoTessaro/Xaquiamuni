using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests
{
    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromeExclusaoCategorias_ComApiESqlDescartavel()
    {
        var repo = EncontrarRepositorio();
        var dist = Path.Combine(repo, "client", "dist", "xaquiamuni", "browser");
        var script = Path.Combine(repo, "server", "tests", "Xaquiamuni.Tests", "Integration", "chrome-compra-smoke.mjs");
        Assert.True(File.Exists(Path.Combine(dist, "index.html")), "Gere o build Angular antes do ensaio.");
        var temporario = Directory.CreateTempSubdirectory(TemporarioPrefixo).FullName;
        try
        {
            await using var banco = await SqlServerTestDatabase.CreateAsync();
            await using var host = new HostE2ECompra(banco, dist, temporario, new RelogioJornadaCompra());
            var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
            var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
            var jornada = new JornadaCompraE2E(host, new RelogioJornadaCompra(), admin.Id, cliente.Email);
            await jornada.SemearAsync(nomeCategoria: "Categoria com produto publicado");
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin, exclusaoCategorias = true,
            }, null);
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}

