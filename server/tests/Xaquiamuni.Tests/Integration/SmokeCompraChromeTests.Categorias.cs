using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;
using Microsoft.EntityFrameworkCore;

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

    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public Task ChromeExclusaoVariante_ComApiESqlDescartavel()
        => ExecutarExclusaoCadastroAsync(fornecedores: false, variantes: true);

    private async Task ExecutarExclusaoCadastroAsync(bool fornecedores, bool variantes = false)
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
            object? exclusaoVariante = null;
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
                if (variantes)
                {
                    await using var db = banco.CreateVerificationContext();
                    var produto = await db.Produtos.Include(p => p.Variantes).Include(p => p.Imagens)
                        .ThenInclude(i => i.Variantes).SingleAsync();
                    var imagem = Assert.Single(produto.Imagens);
                    var extra = ProdutoVariante.Criar("Alecrim", "UN", 20m);
                    produto.AdicionarVariante(extra);
                    db.ProdutoVariantes.Add(extra);
                    foreach (var v in produto.Variantes) produto.AssociarImagemAVariante(imagem.Id, v.Id);
                    await db.SaveChangesAsync();
                    exclusaoVariante = new { produto.Id, produto.Nome, produto.CategoriaId, imagem = imagem.Id,
                        opcao = extra.Id };
                }
            }
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin, exclusaoCategorias = !fornecedores && !variantes,
                insumoFornecedor, exclusaoVariante,
            }, null);
            if (variantes)
            {
                await using var db = banco.CreateVerificationContext();
                Assert.False(await db.Pedidos.AnyAsync());
                Assert.False(await db.ReservasProdutoAcabado.AnyAsync());
            }
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}

