using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests
{
    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromeExclusaoCategorias_ComApiESqlDescartavel()
        => await ExecutarExclusaoCadastroAsync(fornecedores: false);

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromeExclusaoFornecedores_ComApiESqlDescartavel()
        => await ExecutarExclusaoCadastroAsync(fornecedores: true);

    private async Task ExecutarExclusaoCadastroAsync(bool fornecedores)
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
            Guid? insumoFornecedor = null;
            if (fornecedores)
            {
                await using var db = banco.CreateVerificationContext();
                var insumo = InsumoEstoque.Criar("Material de teste", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
                db.InsumosEstoque.Add(insumo);
                await db.SaveChangesAsync();
                insumoFornecedor = insumo.Id;
            }
            else
            {
                var jornada = new JornadaCompraE2E(host, new RelogioJornadaCompra(), admin.Id, cliente.Email);
                await jornada.SemearAsync(nomeCategoria: "Categoria com produto publicado");
            }
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin, exclusaoCategorias = !fornecedores,
                insumoFornecedor,
            }, null);
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}

