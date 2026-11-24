using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class PedidoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task Migration_PreservaReservaLegadaEPersistePedidoComItensEChavesRestritivas()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261118045526_VincularMovimentosRetiradaAsReservas");
        await using var context = database.CreateVerificationContext();
        var agora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero).AddTicks(1);
        var categoria = Categoria.Criar("Artesanais", null, 0);
        await ReservarProdutoAcabadoSqlServerIntegrationTests.InserirCategoriaAnteriorAVersaoAsync(context, categoria);
        var produto = Produto.CriarSimples("Sabonete", null, categoria, "UN", 19.99m);
        var outroProduto = Produto.CriarSimples("Vela", null, categoria, "UN", 10.01m);
        var variante = Assert.Single(produto.Variantes);
        var outraVariante = Assert.Single(outroProduto.Variantes);
        var dia = DateOnly.FromDateTime(agora.DateTime);
        var lote = LoteProdutoAcabado.Criar(variante, null, dia, dia.AddDays(30));
        var reservaLegada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 1, agora);
        context.Produtos.AddRange(produto, outroProduto);
        context.LotesProdutoAcabado.Add(lote);
        context.ReservasProdutoAcabado.Add(reservaLegada);
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync();

        var usuario = new XaquiamuniUser { Id = Guid.NewGuid(), UserName = "Cliente de teste" };
        var cpf = new CpfProtegido(Convert.ToBase64String(new byte[11]), Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]), new string('A', 64), "teste", "teste");
        context.Users.Add(usuario);
        context.ClientePerfis.Add(ClientePerfil.Criar(usuario.Id, "Cliente de teste", cpf));
        var item = new DadosItemPedido(produto.Id, variante.Id, "Sabonete", "Padrão", 2, 1999);
        var outroItem = new DadosItemPedido(outroProduto.Id, outraVariante.Id, "Vela", "Padrão", 1, 1001);
        var pedido = Pedido.Criar(usuario.Id, agora, [item, outroItem]);
        var responsavel = Guid.NewGuid();
        pedido.ConfirmarPagamento(responsavel, agora.AddHours(1));
        pedido.IniciarPreparacao(responsavel, agora.AddDays(1));
        pedido.AutorizarRetirada(responsavel, agora.AddDays(2));
        context.Pedidos.Add(pedido);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var restaurado = await context.Pedidos.Include(p => p.Itens).SingleAsync();
        Assert.Equal(pedido.Id, restaurado.Id);
        Assert.Equal(usuario.Id, restaurado.ClienteId);
        Assert.Equal(agora.AddHours(24), restaurado.PrazoPagamentoEmUtc);
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, restaurado.Situacao);
        Assert.Equal(responsavel, restaurado.RetiradaAutorizadaPorId);
        Assert.Equal(4999, restaurado.TotalCentavos);
        Assert.Equal(4999, restaurado.Itens.Sum(i => i.SubtotalCentavos));
        Assert.Equal("Sabonete", Assert.Single(restaurado.Itens, i => i.ProdutoId == produto.Id).NomeProduto);
        var preservada = await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(reservaLegada.PedidoId, preservada.PedidoId);
        Assert.Equal(reservaLegada.ExpiraEmUtc, preservada.ExpiraEmUtc);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, preservada.Estado);

        context.Pedidos.Add(Pedido.Criar(usuario.Id, agora, [item with { ProdutoVarianteId = outraVariante.Id }]));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.Pedidos.Add(Pedido.Criar(Guid.NewGuid(), agora, [item]));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.Pedidos.CountAsync());
        Assert.Equal(2, await context.ItensPedido.CountAsync());
    }
}
