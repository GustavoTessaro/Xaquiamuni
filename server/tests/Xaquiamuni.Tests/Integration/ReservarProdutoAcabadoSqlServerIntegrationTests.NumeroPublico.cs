using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task NumeroPublico_BackfillPreservaPedidosHistoricosInclusiveCancelados()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261123011231_RegistrarPostagemPostal");
        await using var db = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(db);
        var ativo = Guid.NewGuid();
        var cancelado = Guid.NewGuid();
        var operador = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Pedidos] ([Id], [ClienteId], [Situacao], [CriadoEmUtc], [PrazoPagamentoEmUtc],
                [TotalCentavos], [FreteCentavos], [ModalidadeEntrega], [EncerradoEmUtc], [EncerradoPorId], [MotivoCancelamento])
            VALUES ({ativo}, {cliente}, 1, '2026-11-20T14:00:00+00:00', '2026-11-21T14:00:00+00:00', 1399, 0, 1, NULL, NULL, NULL),
                   ({cancelado}, {cliente}, 6, '2026-11-20T14:00:00+00:00', '2026-11-21T14:00:00+00:00', 2399, 0, 1,
                    '2026-11-20T15:00:00+00:00', {operador}, 'Cancelado no teste');
            """);
        await db.Database.MigrateAsync();

        var pedidos = await db.Pedidos.AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Equal(2, pedidos.Count);
        Assert.All(pedidos.Values, x => Assert.Equal(x.NumeroPublico, NumeroPublicoPedido.Validar(x.NumeroPublico)));
        Assert.NotEqual(pedidos[ativo].NumeroPublico, pedidos[cancelado].NumeroPublico);
        Assert.Equal(1399, pedidos[ativo].TotalCentavos);
        Assert.Equal(SituacaoPedido.AguardandoPagamento, pedidos[ativo].Situacao);
        Assert.Equal(2399, pedidos[cancelado].TotalCentavos);
        Assert.Equal(SituacaoPedido.Cancelado, pedidos[cancelado].Situacao);
        Assert.Equal(operador, pedidos[cancelado].EncerradoPorId);
        var repetido = pedidos[ativo].NumeroPublico;
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Pedidos] SET [NumeroPublico] = {repetido} WHERE [Id] = {cancelado}"));
    }

    [SqlServerIntegrationFact]
    public async Task NumeroPublico_ResolveColisaoECorridaSemDuplicarReservas()
    {
        const string ocupado = "XQ-AAAAAAAAAA";
        const string disputado = "XQ-CCCCCCCCCC";
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var db = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(db);
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(db);
        await EntrarAsync(db, padrao.Id, 3);
        await EntrarAsync(db, alternativa.Id, 2);
        var entrada = new CriarPedidoCompraInput(cliente, Guid.NewGuid(), [new(produto.Id, padrao.Id, 1)]);

        CriarPedidoCompra CompraCom(XaquiamuniDbContext ctx, Func<string> gerar) =>
            new(new EfEstoqueProdutoAcabadoRepository(ctx, new RelogioFixo(), new CalendarioFixo(), gerarNumeroPublico: gerar));

        var primeiro = Assert.IsType<Pedido>((await CompraCom(db, () => ocupado).ExecuteAsync(entrada)).Pedido);
        Assert.Equal(ocupado, primeiro.NumeroPublico);

        var nova = entrada with { ChaveIdempotencia = Guid.NewGuid() };
        var limite = await CompraCom(db, () => ocupado).ExecuteAsync(nova);
        Assert.Equal(CriarPedidoCompraStatus.ConflitoConcorrencia, limite.Status);
        Assert.Equal(1, await db.Pedidos.CountAsync());
        Assert.Equal(1, await db.ReservasProdutoAcabado.CountAsync());

        await using var aba = database.CreateVerificationContext();
        var opcoesA = new Queue<string>([ocupado, disputado, "XQ-EEEEEEEEEE"]);
        var opcoesB = new Queue<string>([disputado, "XQ-DDDDDDDDDD"]);
        var corrida = await Task.WhenAll(
            CompraCom(db, opcoesA.Dequeue).ExecuteAsync(nova),
            CompraCom(aba, opcoesB.Dequeue).ExecuteAsync(nova with
            {
                ChaveIdempotencia = Guid.NewGuid(), Itens = [new(produto.Id, alternativa.Id, 1)],
            }));
        Assert.All(corrida, x => Assert.Equal(CriarPedidoCompraStatus.Sucesso, x.Status));
        Assert.Contains(corrida, x => x.Pedido!.NumeroPublico == disputado);
        Assert.Equal(3, await db.Pedidos.Select(x => x.NumeroPublico).Distinct().CountAsync());
        Assert.Equal(3, await db.ReservasProdutoAcabado.CountAsync());
        var replay = await CompraCom(db, () => throw new InvalidOperationException()).ExecuteAsync(nova);
        Assert.True(replay.Repetida);
        Assert.Equal(corrida[0].Pedido!.NumeroPublico, replay.Pedido!.NumeroPublico);
    }
}
