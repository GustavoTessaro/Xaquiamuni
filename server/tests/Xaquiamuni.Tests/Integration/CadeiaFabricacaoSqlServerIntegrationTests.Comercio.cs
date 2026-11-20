using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private static async Task<PagamentoFornecedorDto> PagarCompraDaCadeiaAsync(
        Sessao admin, XaquiamuniDbContext db, Dados dados)
    {
        var compraId = dados.Compra.Resumo.Id;
        var url = Financeiro + $"/compras/{compraId:D}";
        var recebida = (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!;
        Assert.Equal(("Recebida", "Pendente", "4650", "0", "4650"),
            (recebida.SituacaoCompra, recebida.SituacaoFinanceira, recebida.TotalCentavos, recebida.PagoCentavos, recebida.SaldoCentavos));
        Assert.Empty((await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!.Itens);

        var comando = new PagarFornecedorInput(dados.Hoje.AddDays(-1), "Pix", "Parcela de fornecedor", 2000);
        var chave = Guid.NewGuid();
        var pagamento = await admin.PostAsync<PagamentoFornecedorDto>(url + "/pagamentos", comando, HttpStatusCode.OK, chave);
        var repetido = await admin.PostAsync<PagamentoFornecedorDto>(url + "/pagamentos", comando, HttpStatusCode.OK, chave);
        Assert.Equal(pagamento, repetido);
        Assert.Equal(1, await db.Set<PagamentoFornecedor>().CountAsync());
        var parcial = (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!;
        Assert.Equal(("Parcial", "2000", "2650"),
            (parcial.SituacaoFinanceira, parcial.PagoCentavos, parcial.SaldoCentavos));
        await ConferirFisicoAsync(db, dados, 200, 18, 6);
        Assert.Equal(4, await db.MovimentosInsumo.CountAsync());
        return pagamento;
    }

    private static async Task ConciliarComercioEFinanceiroAsync(Sessao admin, XaquiamuniDbContext db,
        Dados dados, Guid pedidoId, Guid loteProducao, PagamentoFornecedorDto pagamento)
    {
        var antesPix = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!;
        Assert.Equal(pagamento.Id, Assert.Single(antesPix.Itens).EventoId);
        var pixUrl = $"/api/admin/pedidos/{pedidoId:D}/confirmar-pagamento";
        var confirmacao = new { recebimentoConferido = true };
        var confirmado = await admin.PostAsync<JsonElement>(pixUrl, confirmacao, HttpStatusCode.OK);
        var pixRepetido = await admin.PostAsync<JsonElement>(pixUrl, confirmacao, HttpStatusCode.OK);
        Assert.Equal("PagamentoConfirmado", confirmado.GetProperty("situacao").GetString());
        Assert.True(pixRepetido.GetProperty("repetida").GetBoolean());
        var pedido = await db.Pedidos.AsNoTracking().SingleAsync();
        var dataPix = DateOnly.FromDateTime(pedido.PagamentoConfirmadoEmUtc!.Value.UtcDateTime);
        Assert.Equal(1199L, pedido.TotalCentavos);
        var apenasPix = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes + "?origem=PedidoSite"))!;
        Assert.Equal(pedidoId, Assert.Single(apenasPix.Itens).EventoId);

        var estoqueUrl = $"/api/admin/estoque/produtos-acabados/variantes/{dados.VarianteId:D}";
        // Um segundo lote, com validade posterior, permite observar FEFO na venda real.
        var entrada = await admin.PostAsync<JsonElement>(estoqueUrl + "/entradas", new EntradaProdutoAcabadoAdminRequest
        {
            Tipo = TipoMovimentoProdutoAcabado.EntradaManual,
            Quantidade = 1,
            CondicaoDestino = CondicaoEstoqueProdutoAcabado.Vendavel,
            DataFabricacao = dados.Hoje.AddDays(-1),
            DataValidade = dados.Hoje.AddDays(60),
            Motivo = "Lote com validade posterior para a regressão FEFO"
        }, HttpStatusCode.Created, Guid.NewGuid());
        var lotePosterior = entrada.GetProperty("loteId").GetGuid();

        const string vendas = "/api/admin/vendas-externas";
        var dataEfetiva = dados.Hoje.AddDays(-1);
        var vendaInput = new CriarVendaExternaInput("Presencial", null, dataEfetiva, "Cliente de teste", null,
            [new(dados.ProdutoId, dados.VarianteId, 2, 900)], null);
        using var excedente = await admin.EnviarAsync(vendas,
            vendaInput with { Itens = [new(dados.ProdutoId, dados.VarianteId, 4, 900)] }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, excedente.StatusCode);
        Assert.False(await db.Set<VendaExterna>().AnyAsync());
        var chaveVenda = Guid.NewGuid();
        var venda = await admin.PostAsync<ResumoVendaExterna>(vendas, vendaInput, HttpStatusCode.Created, chaveVenda);
        Assert.Equal(venda, await admin.PostAsync<ResumoVendaExterna>(vendas, vendaInput, HttpStatusCode.OK, chaveVenda));
        Assert.Equal(("Pendente", "1800", "0", "1800"),
            (venda.SituacaoPagamento, venda.TotalCentavos, venda.RecebidoCentavos, venda.SaldoCentavos));

        var recebimento = new ReceberVendaExternaInput(400, dataEfetiva, "Dinheiro");
        var chaveRecebimento = Guid.NewGuid();
        var urlVenda = vendas + $"/{venda.Id:D}";
        var vendida = await admin.PostAsync<ResumoVendaExterna>(urlVenda + "/recebimentos", recebimento, HttpStatusCode.OK, chaveRecebimento);
        Assert.Equal(vendida, await admin.PostAsync<ResumoVendaExterna>(urlVenda + "/recebimentos", recebimento, HttpStatusCode.OK, chaveRecebimento));
        Assert.Equal(("Parcial", "400", "1400"), (vendida.SituacaoPagamento, vendida.RecebidoCentavos, vendida.SaldoCentavos));

        var detalhe = await admin.Http.GetFromJsonAsync<JsonElement>(urlVenda);
        Assert.Equal("900", Assert.Single(detalhe.GetProperty("itens").EnumerateArray()).GetProperty("precoUnitarioCentavos").GetString());
        var baixa = Assert.Single(detalhe.GetProperty("baixas").EnumerateArray());
        Assert.Equal(loteProducao, baixa.GetProperty("loteId").GetGuid());
        Assert.Equal(2, baixa.GetProperty("quantidade").GetInt32());
        Assert.Equal(1, await db.Set<VendaExterna>().CountAsync());
        Assert.Equal(1, await db.Set<RecebimentoVendaExterna>().CountAsync());
        Assert.Equal(1, await db.Set<BaixaVendaExterna>().CountAsync());
        Assert.Equal(3, await db.MovimentosProdutoAcabado.CountAsync());
        var reserva = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal((pedidoId, loteProducao, 1, EstadoReservaProdutoAcabado.Confirmada),
            (reserva.PedidoId, reserva.LoteId, reserva.Quantidade, reserva.Estado));
        var saldos = await db.SaldosProdutoAcabado.AsNoTracking().ToDictionaryAsync(s => s.LoteId, s => s.QuantidadeVendavel);
        Assert.Equal(1, saldos[loteProducao]);
        Assert.Equal(1, saldos[lotePosterior]);
        var posicao = (await admin.Http.GetFromJsonAsync<PosicaoProdutoAcabadoAdminResponse>(estoqueUrl + "/posicao"))!;
        Assert.Equal((2, 1, 1), (posicao.SaldoFisico, posicao.SaldoReservado, posicao.SaldoDisponivel));

        var mov = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!;
        Assert.Equal(3, mov.Itens.Count);
        var pix = Assert.Single(mov.Itens, m => m.Origem == "PedidoSite");
        Assert.Equal((pedidoId, pedidoId, "Entrada", "1199", "ConfirmacaoUtc", dataPix),
            (pix.EventoId, pix.RecursoId, pix.Tipo, pix.ValorCentavos, pix.NaturezaData, pix.Data));
        var fornecedor = Assert.Single(mov.Itens, m => m.Origem == "PagamentoCompra");
        Assert.Equal((pagamento.Id, dados.Compra.Resumo.Id, "Saida", "2000", "Efetiva", dataEfetiva),
            (fornecedor.EventoId, fornecedor.RecursoId, fornecedor.Tipo, fornecedor.ValorCentavos, fornecedor.NaturezaData, fornecedor.Data));
        var externo = Assert.Single(mov.Itens, m => m.Origem == "VendaExterna");
        var recebimentoId = Assert.Single(detalhe.GetProperty("recebimentos").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal((recebimentoId, venda.Id, "Entrada", "400", "Efetiva", dataEfetiva),
            (externo.EventoId, externo.RecursoId, externo.Tipo, externo.ValorCentavos, externo.NaturezaData, externo.Data));

        var resumo = await ObterResumoAsync(admin, "");
        Assert.Equal(new FluxoFinanceiroDto(null, null, "1599", "2000", "-401"), resumo.FluxoPeriodo);
        Assert.Equal(new PosicaoFinanceiraDto("1400", "2650", 0), resumo.PosicaoAtual);
        var dia = dataEfetiva.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.True(dataPix > dataEfetiva);
        var filtrado = await ObterResumoAsync(admin, $"?dataInicial={dia}&dataFinal={dia}");
        Assert.Equal(new FluxoFinanceiroDto(dataEfetiva, dataEfetiva, "400", "2000", "-1600"), filtrado.FluxoPeriodo);
        Assert.Equal(resumo.PosicaoAtual, filtrado.PosicaoAtual);
        Assert.Equal(1, await db.Set<PagamentoFornecedor>().CountAsync());
        Assert.Equal(8, await db.MovimentosInsumo.CountAsync());
        Assert.False(await db.Set<LancamentoFinanceiro>().AnyAsync());
        await ConferirFisicoAsync(db, dados, 125, 7, 2);
    }
}
