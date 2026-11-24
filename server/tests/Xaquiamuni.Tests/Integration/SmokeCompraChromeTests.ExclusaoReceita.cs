using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests
{
    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromeExclusaoReceita_ComApiESqlDescartavel()
    {
        var repo = EncontrarRepositorio();
        var dist = Path.Combine(repo, "client", "dist", "xaquiamuni", "browser");
        var script = Path.Combine(repo, "server", "tests", "Xaquiamuni.Tests", "Integration", "chrome-compra-smoke.mjs");
        var temporario = Directory.CreateTempSubdirectory(TemporarioPrefixo).FullName;
        try
        {
            await using var banco = await SqlServerTestDatabase.CreateAsync();
            await using var host = new HostE2ECompra(banco, dist, temporario);
            var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
            var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
            await using var db = banco.CreateVerificationContext();
            var (_, variante, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin,
                exclusaoReceita = new { variante = variante.Id, extra = extra.Id }
            }, null);
            Assert.Equal(1, await db.ReceitasProducao.CountAsync());
            Assert.Equal(1, await db.OrdensProducao.CountAsync());
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoReceita").SingleAsync());
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}
