using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests
{
    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromeExclusaoInsumo_ComApiESqlDescartavel()
    {
        var repo = EncontrarRepositorio();
        var dist = Path.Combine(repo, "client", "dist", "xaquiamuni", "browser");
        var script = Path.Combine(repo, "server", "tests", "Xaquiamuni.Tests", "Integration", "chrome-compra-smoke.mjs");
        Assert.True(File.Exists(Path.Combine(dist, "index.html")), "Gere o build Angular antes do ensaio.");
        var temporario = Directory.CreateTempSubdirectory(TemporarioPrefixo).FullName;
        try
        {
            await using var banco = await SqlServerTestDatabase.CreateAsync();
            await using var host = new HostE2ECompra(banco, dist, temporario);
            var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
            var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
            await using var db = banco.CreateVerificationContext();
            var fornecedor = Fornecedor.Criar("Fornecedor Chrome de teste", null, null, null, DateTimeOffset.UtcNow);
            db.Fornecedores.Add(fornecedor);
            await db.SaveChangesAsync();
            var (_, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin,
                exclusaoInsumo = new { fornecedor = fornecedor.Id, variante = variante.Id }
            }, null);
            Assert.Equal(1, await db.ComprasMateriasPrimas.CountAsync());
            Assert.Equal(1, await db.ReceitasProducao.CountAsync());
            Assert.Equal(1, await db.OrdensProducao.CountAsync());
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}
