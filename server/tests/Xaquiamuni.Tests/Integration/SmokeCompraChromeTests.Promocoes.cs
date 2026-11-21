using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class SmokeCompraChromeTests
{
    [ChromeCompraFact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ChromePromocoes_ComApiSqlEConciliacaoAteEntrega()
    {
        var repo = EncontrarRepositorio();
        var dist = Path.Combine(repo, "client", "dist", "xaquiamuni", "browser");
        var script = Path.Combine(repo, "server", "tests", "Xaquiamuni.Tests", "Integration", "chrome-compra-smoke.mjs");
        Assert.True(File.Exists(Path.Combine(dist, "index.html")), "Gere o build Angular antes do ensaio.");
        var temporario = Directory.CreateTempSubdirectory(TemporarioPrefixo).FullName;
        try
        {
            await using var banco = await SqlServerTestDatabase.CreateAsync();
            var relogio = new RelogioJornadaCompra();
            await using var host = new HostE2ECompra(banco, dist, temporario, relogio, promocoesComerciais: true);
            var admin = await host.CriarContaAsync(XaquiamuniRoles.Administrador);
            var cliente = await host.CriarContaAsync(XaquiamuniRoles.Cliente);
            var fluxo = new JornadaCompraE2E(host, relogio, admin.Id, cliente.Email);
            var produto = await fluxo.SemearAsync(comVariante: true);
            var inicio = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 * 60).AddMinutes(10);
            var fim = inicio.AddMinutes(10);
            await RodarChromeAsync(script, new
            {
                origin = host.Origin, pinSpki = host.PinSpki, chromePath = DemonstracaoLocal.CaminhoChrome(),
                profile = Path.Combine(temporario, "chrome"), cliente, admin, promocoes = produto, inicio, fim,
            }, null, checkpoint: etapa =>
            {
                relogio.Definir(etapa switch
                {
                    "inicio-promocao" => inicio,
                    "fim-promocao" => fim,
                    _ => throw new InvalidOperationException("Checkpoint de promoção inesperado.")
                });
                return Task.FromResult<object>(true);
            });

            await using var db = banco.CreateVerificationContext();
            var pedidos = await db.Pedidos.Include(p => p.Itens).ToListAsync();
            Assert.Equal(2, pedidos.Count);
            var entregue = Assert.Single(pedidos, p => p.Situacao == SituacaoPedido.Entregue);
            var cancelado = Assert.Single(pedidos, p => p.Situacao == SituacaoPedido.Cancelado);
            Assert.Equal(1190, entregue.TotalCentavos);
            Assert.Equal(1390, cancelado.TotalCentavos);
            var item = Assert.Single(entregue.Itens);
            Assert.Equal(1190, item.PrecoUnitarioCentavos);
            Assert.Equal(1390, item.PrecoRegularCentavos);
            Assert.NotNull(item.PromocaoId);
            var reservas = await db.ReservasProdutoAcabado.ToListAsync();
            Assert.Equal(2, reservas.Count);
            Assert.Equal(EstadoReservaProdutoAcabado.Consumida, Assert.Single(reservas, r => r.PedidoId == entregue.Id).Estado);
            Assert.Equal(EstadoReservaProdutoAcabado.Liberada, Assert.Single(reservas, r => r.PedidoId == cancelado.Id).Estado);
            var saldos = await db.SaldosProdutoAcabado.ToListAsync();
            Assert.Equal(9, saldos.Sum(s => s.QuantidadeFisica));
        }
        finally
        {
            RemoverTemporario(temporario);
        }
    }
}
