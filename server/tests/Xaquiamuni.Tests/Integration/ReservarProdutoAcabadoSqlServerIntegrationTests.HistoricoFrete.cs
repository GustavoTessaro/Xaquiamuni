using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task PromocoesFrete_SequenciaDeMigrationsPreservaRevisoesPedidoPostalEReserva()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261123011231_RegistrarPostagemPostal");
        await using var db = banco.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(db);
        var (produto, variante, _) = await CriarProdutoCompravelAsync(db);
        var lote = (await EntrarAsync(db, variante.Id, 3)).Lote!;
        var cotacaoId = Guid.NewGuid();
        var publicada = Agora.AddHours(-1);
        var validade = Agora.AddHours(24);
        var destino = new EnderecoEntregaPedido("95200000", "RS", "Vacaria", "Centro", "Rua Histórica", "12", null, "4322509");
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var itens = JsonSerializer.Serialize(new[] { new ItemCongeladoCorreios(produto.Id, variante.Id, 2, "Sabonete", "Padrão") }, web);
        var endereco = JsonSerializer.Serialize(destino, web);
        var impressao = new string('A', 64);
        var entrega = new DadosEntregaPedido(Destino: destino,
            PropostaPostal: new DadosPropostaPostal(cotacaoId, 2, "SEDEX", 4, publicada, validade));
        var pedido = Pedido.Criar(cliente, Agora,
            [new DadosItemPedido(produto.Id, variante.Id, "Sabonete", "Padrão", 2, 1199)],
            entrega: entrega, freteCentavos: 1400, modalidade: ModalidadeEntregaPedido.Correios);
        var entregaJson = JsonSerializer.Serialize(entrega);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [SolicitacoesCorreios] ([Id], [ClienteId], [CriadaEmUtc], [ItensJson], [EnderecoJson],
                [Impressao], [Versao], [Situacao], [Revisao], [RevisaoAceita], [ValidaAteUtc])
            VALUES ({cotacaoId}, {cliente}, {publicada.AddHours(-1)}, {itens}, {endereco}, {impressao}, 5, 7, 2, 2, {validade});
            INSERT INTO [PropostasCorreios] ([SolicitacaoId], [Revisao], [Servico], [FreteCentavos], [PrazoDias],
                [ValidaAteUtc], [PesoTotalGramas], [VolumesJson], [PublicadaEmUtc], [PublicadaPor])
            VALUES ({cotacaoId}, 1, 'PAC', 1200, 5, {validade}, 400, '[]', {publicada.AddMinutes(-5)}, {Responsavel}),
                   ({cotacaoId}, 2, 'SEDEX', 1400, 4, {validade}, 400, '[]', {publicada}, {Responsavel});
            INSERT INTO [Pedidos] ([Id], [ClienteId], [Situacao], [CriadoEmUtc], [PrazoPagamentoEmUtc],
                [TotalCentavos], [FreteCentavos], [ModalidadeEntrega], [Entrega], [CotacaoCorreiosId], [RevisaoCotacaoCorreios])
            VALUES ({pedido.Id}, {cliente}, 1, {Agora}, {pedido.PrazoPagamentoEmUtc}, 3798, 1400, 3, {entregaJson}, {cotacaoId}, 2);
            """);
        db.ItensPedido.AddRange(pedido.Itens);
        var reserva = ReservaProdutoAcabado.Criar(pedido.Id, variante.Id, lote.Id, 2, Agora);
        db.ReservasProdutoAcabado.Add(reserva);
        await db.SaveChangesAsync();

        Assert.Equal(new[] { "20261123200209_AdicionarNumeroPublicoPedido", "20261123202620_CriarPromocoesFrete",
            "20261123225109_CongelarDescontoFretePostal" }, await db.Database.GetPendingMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var campanha = PromocaoFrete.Criar(new("Campanha posterior", TipoDescontoFrete.FreteGratis, 0,
            ModalidadesPromocaoFrete.CorreiosManual, 0, null, null), Responsavel, Agora);
        campanha.AlterarStatus(true, Responsavel, Agora);
        db.Add(campanha);
        await db.SaveChangesAsync();

        var preservado = await db.Pedidos.AsNoTracking().Include(x => x.Itens).SingleAsync();
        Assert.Equal(pedido.Id, preservado.Id);
        Assert.Equal(preservado.NumeroPublico, NumeroPublicoPedido.Validar(preservado.NumeroPublico));
        Assert.Equal((2398L, 1400L, 3798L), (preservado.SubtotalProdutosCentavos, preservado.FreteCentavos, preservado.TotalCentavos));
        Assert.Equal(entrega, preservado.Entrega);
        Assert.Equal(2, Assert.Single(preservado.Itens).Quantidade);
        var reservaPreservada = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(reserva.Id, reservaPreservada.Id);
        Assert.Equal(pedido.Id, reservaPreservada.PedidoId);
        Assert.Equal(reserva.LoteId, reservaPreservada.LoteId);
        Assert.Equal(2, reservaPreservada.Quantidade);
        Assert.Equal(reserva.Estado, reservaPreservada.Estado);

        using var host = CriarApiPedidos(banco);
        using var sessao = ClienteHttpPedidos(host, cliente);
        var cotacao = (await sessao.GetFromJsonAsync<DetalheCotacaoCorreios>($"/api/cotacoes-correios/{cotacaoId:D}"))!;
        Assert.Equal(pedido.Id, cotacao.PedidoConvertidoId);
        Assert.Equal(2, cotacao.RevisaoAceita);
        Assert.Equal(5, cotacao.Versao);
        Assert.Equal(new long[] { 1400, 1200 }, cotacao.Propostas.Select(x => x.FreteCentavos));
        Assert.All(cotacao.Propostas, x =>
        {
            Assert.Null(x.Valores?.DescontoFrete);
            Assert.Null(x.Valores?.TotalCentavos);
        });
    }
}
