using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string Movimentacoes = "/api/admin/financeiro/movimentacoes";

    [SqlServerIntegrationFact]
    public async Task Movimentacoes_CompoeCaixaSemDuplicarEventosNemMovimentarOrigens()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        var contaCliente = await ambiente.Host.CriarContaAsync(XaquiamuniRoles.Cliente);
        var autor = (await ambiente.Host.CriarContaAsync(XaquiamuniRoles.Administrador)).Id;
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        await admin.PostAsync<JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        var compraPaga = await admin.PostAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>(Compras,
            new CompraMateriaPrimaInput(dados.FornecedorId, dados.Hoje,
                [new(null, 1, 25m, [new(dados.Extrato, null, 1, null)])], null), HttpStatusCode.Created, Guid.NewGuid());
        var pagamentos = Financeiro + $"/compras/{compraPaga.Resumo.Id:D}/pagamentos";
        var inicio = new DateOnly(2026, 10, 1);
        var fim = inicio.AddDays(1);
        var pago1 = await admin.PostAsync<PagamentoFornecedorDto>(pagamentos, new PagarFornecedorInput(inicio, "Boleto", null, 900),
            HttpStatusCode.OK, Guid.NewGuid());
        var pago2 = await admin.PostAsync<PagamentoFornecedorDto>(pagamentos, new PagarFornecedorInput(fim, "Transferencia", null, 1600),
            HttpStatusCode.OK, Guid.NewGuid());

        var instante = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var variantes = await db.ProdutoVariantes.Include(v => v.Produto).Where(v => v.ProdutoId == dados.ProdutoId).ToArrayAsync();
        var itens = variantes.Select(v => new DadosItemPedido(v.ProdutoId, v.Id, v.Produto.Nome, v.Identificacao, 1, 1250)).ToArray();
        Pedido NovoPedido() => Pedido.Criar(contaCliente.Id, instante.AddHours(-2), itens);
        var confirmado = NovoPedido();
        confirmado.ConfirmarPagamento(autor, instante);
        confirmado.IniciarPreparacao(autor, instante.AddHours(1));
        var pendente = NovoPedido();
        var expirado = NovoPedido();
        expirado.Expirar(instante.AddDays(1));
        var cancelado = NovoPedido();
        cancelado.ConfirmarPagamento(autor, instante);
        cancelado.Cancelar(autor, instante.AddHours(1), "Cancelado");
        db.Pedidos.AddRange(confirmado, pendente, expirado, cancelado);

        VendaExterna NovaVenda() => VendaExterna.Criar(CanalVendaExterna.Presencial, null, inicio, "Cliente teste",
            null, variantes.Select(v => new ItemVendaExterna(v, 1, 1500, 0)), autor, instante);
        var venda = NovaVenda();
        var receb1 = venda.Receber(500, inicio, MeioRecebimentoVendaExterna.Cartao, autor, Guid.NewGuid(), instante);
        var receb2 = venda.Receber(2500, fim, MeioRecebimentoVendaExterna.Dinheiro, autor, Guid.NewGuid(), instante);
        db.AddRange(venda, NovaVenda());

        var entrada = LancamentoFinanceiro.Criar(TipoLancamento.Entrada, "Teste", "Aporte exato", long.MaxValue,
            inicio, MeioFinanceiro.Pix, autor, instante);
        var saida = LancamentoFinanceiro.Criar(TipoLancamento.Saida, "Teste", "Saída fora do período", 1,
            fim.AddDays(1), MeioFinanceiro.Outro, autor, instante);
        var manualCancelado = LancamentoFinanceiro.Criar(TipoLancamento.Entrada, "Teste", "Cancelado", 100,
            fim, MeioFinanceiro.Pix, autor, instante);
        manualCancelado.Cancelar("Teste", autor, instante);
        db.AddRange(entrada, saida, manualCancelado);
        await db.SaveChangesAsync();

        using var bloqueado = await cliente.Http.GetAsync(Movimentacoes);
        Assert.Equal(HttpStatusCode.Forbidden, bloqueado.StatusCode);
        var pagina = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!;
        Assert.Equal(7, pagina.Itens.Count);
        Assert.Equal(7, pagina.Itens.Select(m => (m.Origem, m.EventoId)).Distinct().Count());
        var pedido = Assert.Single(pagina.Itens, m => m.Origem == "PedidoSite");
        Assert.Equal((confirmado.Id, confirmado.Id, "2500", fim, "ConfirmacaoUtc", "Pix"),
            (pedido.EventoId, pedido.RecursoId, pedido.ValorCentavos, pedido.Data, pedido.NaturezaData, pedido.Meio));
        Assert.Equal("9223372036854775807", Assert.Single(pagina.Itens, m => m.EventoId == entrada.Id).ValorCentavos);
        Assert.Equal(new[] { "Boleto", "Transferencia" }, new[] { pago1.Id, pago2.Id }.Select(id =>
            Assert.Single(pagina.Itens, m => m.EventoId == id && m.RecursoId == compraPaga.Resumo.Id).Meio));
        Assert.Equal(new[] { "Cartao", "Dinheiro" }, new[] { receb1.Id, receb2.Id }.Select(id =>
            Assert.Single(pagina.Itens, m => m.EventoId == id && m.RecursoId == venda.Id).Meio));
        Assert.DoesNotContain(pagina.Itens, m => m.RecursoId == dados.Compra.Resumo.Id);

        var filtrada = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(
            Movimentacoes + "?dataInicial=2026-10-01&dataFinal=2026-10-02&tipo=Saida"))!;
        Assert.Equal(new[] { pago2.Id, pago1.Id }, filtrada.Itens.Select(m => m.EventoId));
        var fronteira = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(
            Movimentacoes + "?dataInicial=2026-10-02&dataFinal=2026-10-02&origem=PedidoSite&tipo=Entrada"))!;
        Assert.Equal(pedido, Assert.Single(fronteira.Itens));
        Assert.Equal(pagina.Itens, (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!.Itens);
        Assert.Equal(3, await db.Set<LancamentoFinanceiro>().CountAsync());
        Assert.Equal(2, await db.Set<PagamentoFornecedor>().CountAsync());
        Assert.Equal(2, await db.Set<RecebimentoVendaExterna>().CountAsync());

        var resumoUrl = Financeiro + "/resumo";
        using var resumoBloqueado = await cliente.Http.GetAsync(resumoUrl);
        Assert.Equal(HttpStatusCode.Forbidden, resumoBloqueado.StatusCode);
        var resumo = await ObterResumoAsync(admin, "?dataInicial=2026-10-01&dataFinal=2026-10-02");
        Assert.Equal(new FluxoFinanceiroDto(inicio, fim, "9223372036854781307", "2500", "9223372036854778807"),
            resumo.FluxoPeriodo);
        Assert.Equal(("3000", "4650", 0L), (resumo.PosicaoAtual.AReceberCentavos, resumo.PosicaoAtual.APagarCentavos,
            resumo.PosicaoAtual.ComprasValorAConferir));
        var apenasFim = await ObterResumoAsync(admin, "?dataInicial=2026-10-02&dataFinal=2026-10-02");
        Assert.Equal(new FluxoFinanceiroDto(fim, fim, "5000", "1600", "3400"), apenasFim.FluxoPeriodo);
        Assert.Equal(resumo.PosicaoAtual, apenasFim.PosicaoAtual);
        Assert.Equal(3, await db.Set<LancamentoFinanceiro>().CountAsync());
        Assert.Equal(2, await db.Set<PagamentoFornecedor>().CountAsync());
        Assert.Equal(2, await db.Set<RecebimentoVendaExterna>().CountAsync());
        Assert.Equal(4, await db.Pedidos.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Movimentacoes_PaginaComDesempateEstavelEValidaFiltros()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var autor = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var data = DateOnly.FromDateTime(agora.UtcDateTime);
        db.AddRange(Enumerable.Range(1, 52).Select(n => LancamentoFinanceiro.Criar(TipoLancamento.Entrada,
            "Teste", $"Movimento {n}", n, data, MeioFinanceiro.Pix, autor, agora)));
        await db.SaveChangesAsync();
        var idsEsperados = await db.Set<LancamentoFinanceiro>().OrderByDescending(m => m.Id).Select(m => m.Id).ToArrayAsync();
        var primeira = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!;
        var segunda = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes + "?pagina=2"))!;
        Assert.True(primeira.TemProxima);
        Assert.False(segunda.TemProxima);
        Assert.Equal(50, primeira.Itens.Count);
        Assert.Equal(idsEsperados, primeira.Itens.Concat(segunda.Itens).Select(m => m.EventoId));

        foreach (var filtro in new[] { "?tipo=1", "?origem=Outra", "?pagina=0", "?dataInicial=2026-10-02&dataFinal=2026-10-01" })
        {
            using var resposta = await admin.Http.GetAsync(Movimentacoes + filtro);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }
    }
}
