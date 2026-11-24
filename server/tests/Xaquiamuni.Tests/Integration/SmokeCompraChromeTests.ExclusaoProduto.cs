using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests
{
    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromeExclusaoProduto_ComApiESqlEImagensDescartaveis()
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
            await using var db = banco.CreateVerificationContext();
            var categoria = Categoria.Criar("Categoria preservada", null, 0);
            db.Categorias.Add(categoria);
            await db.SaveChangesAsync();
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin, exclusaoProduto = categoria.Id,
            }, null);

            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM LimpezasImagensProdutos")
                       .SingleAsync(limite.Token) > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(250), limite.Token);
            var imagem = await db.ProdutoImagens.AsNoTracking().SingleAsync();
            var raiz = Path.Combine(temporario, "imagens");
            Assert.Equal(Path.Combine(raiz, imagem.ChaveArmazenamento.Replace('/', Path.DirectorySeparatorChar)),
                Assert.Single(Directory.GetFiles(Path.Combine(raiz, "product-images"))));
            Assert.True(await db.Categorias.AnyAsync(c => c.Id == categoria.Id));
            Assert.False(await db.Set<ProdutoCaracteristica>().AnyAsync());
            Assert.False(await db.Set<ProdutoCaracteristicaValor>().AnyAsync());
            Assert.False(await db.Set<ProdutoVarianteCaracteristicaValor>().AnyAsync());
            Assert.False(await db.Pedidos.AnyAsync());
            Assert.False(await db.ReservasProdutoAcabado.AnyAsync());
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}
