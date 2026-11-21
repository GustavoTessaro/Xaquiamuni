using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Agenda;
using Xaquiamuni.Aplicacao.Dashboard;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task Dashboard_ConciliaPrazosReservasInsumosComprasProducaoFinanceiroEAgenda()
    {
        const string rota = "/api/admin/dashboard";
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        foreach (var secao in new[] { "operacao", "estoque", "agenda" })
        {
            using var resposta = await cliente.Http.GetAsync(rota + "/" + secao);
            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }

        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        await ReceberEIniciarAsync(admin, dados);
        await PagarCompraDaCadeiaAsync(admin, db, dados);

        var hoje = ambiente.Host.Services.GetRequiredService<ICalendarioOperacionalEstoque>().ObterDataAtual();
        await admin.PostAsync<JsonElement>(Compras, new CompraMateriaPrimaInput(dados.FornecedorId, hoje,
            [new(null, 1, null, [new(dados.Base, null, 1, null)])], null), HttpStatusCode.Created, Guid.NewGuid());
        var baseG = await db.InsumosEstoque.SingleAsync(i => i.Id == dados.Base);
        baseG.AtualizarDados(baseG.Nome, baseG.Tipo, QuantidadeEstoque.Criar(6, UnidadeFisicaEstoque.Grama), false, false);
        db.InsumosEstoque.Add(InsumoEstoque.Criar("Insumo zerado", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama));
        await db.SaveChangesAsync();

        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{dados.VarianteId:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 1, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste dashboard" },
            HttpStatusCode.Created, Guid.NewGuid());
        var comSaldo = await admin.Http.GetFromJsonAsync<EstoqueDashboard>(rota + "/estoque");
        Assert.Equal(new EstoqueDashboard(2, 1), comSaldo);
        await cliente.PostAsync<JsonElement>(Pedidos,
            new { itens = new[] { new { produtoId = dados.ProdutoId, produtoVarianteId = dados.VarianteId, quantidade = 1 } } },
            HttpStatusCode.Created, Guid.NewGuid());
        Assert.Equal(new EstoqueDashboard(2, 0), await admin.Http.GetFromJsonAsync<EstoqueDashboard>(rota + "/estoque"));

        var clienteId = (await cliente.Http.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("userId").GetGuid();
        var adminId = (await admin.Http.GetFromJsonAsync<JsonElement>("/api/admin/auth/me")).GetProperty("userId").GetGuid();
        var agora = DateTimeOffset.UtcNow;
        DadosItemPedido item = new(dados.ProdutoId, dados.VarianteId, "Sabonete", "Padrao", 1, 1199);
        var vencido = Pedido.Criar(clienteId, agora.AddDays(-2), [item]);
        var aPreparar = Pedido.Criar(clienteId, agora.AddHours(-2), [item]);
        var emPreparacao = Pedido.Criar(clienteId, agora.AddHours(-2), [item]);
        var pronto = Pedido.Criar(clienteId, agora.AddHours(-2), [item]);
        foreach (var pedido in new[] { aPreparar, emPreparacao, pronto })
            pedido.ConfirmarPagamento(adminId, agora.AddHours(-1));
        emPreparacao.IniciarPreparacao(adminId, agora.AddMinutes(-40));
        pronto.IniciarPreparacao(adminId, agora.AddMinutes(-40));
        pronto.AutorizarRetirada(adminId, agora.AddMinutes(-20));
        db.Pedidos.AddRange(vencido, aPreparar, emPreparacao, pronto);
        await db.SaveChangesAsync();

        using var operacao = await admin.Http.GetAsync(rota + "/operacao");
        Assert.True(operacao.Headers.CacheControl?.NoStore);
        Assert.Equal(new OperacaoDashboard(1, 1, 1, 1, 1, 1, 1),
            await operacao.Content.ReadFromJsonAsync<OperacaoDashboard>());

        var compromisso = new CompromissoAgendaInput("Feira", null, null, hoje.AddDays(1), null, null, null);
        await admin.PostAsync<JsonElement>(AgendaUrl, compromisso, HttpStatusCode.Created, Guid.NewGuid());
        await admin.PostAsync<JsonElement>(AgendaUrl, compromisso with { Titulo = "Entrega", HorarioInicial = new(9, 30) },
            HttpStatusCode.Created, Guid.NewGuid());
        await admin.PostAsync<JsonElement>(AgendaUrl, compromisso with { Data = hoje.AddDays(-1) },
            HttpStatusCode.Created, Guid.NewGuid());
        var agenda = await admin.Http.GetFromJsonAsync<CompromissoDashboard[]>(rota + "/agenda");
        Assert.Equal(new CompromissoDashboard[] { new(hoje.AddDays(1), null, "Feira"), new(hoje.AddDays(1), new(9, 30), "Entrega") }, agenda);
        var financeiro = (await admin.Http.GetFromJsonAsync<ResumoFinanceiroDto>("/api/admin/financeiro/resumo"))!;
        Assert.Equal(("3597", "2000", "1597"),
            (financeiro.FluxoPeriodo.EntradasRecebidasCentavos, financeiro.FluxoPeriodo.SaidasPagasCentavos, financeiro.FluxoPeriodo.MovimentacaoLiquidaCentavos));
        Assert.Equal(("2650", 1L), (financeiro.PosicaoAtual.APagarCentavos, financeiro.PosicaoAtual.ComprasValorAConferir));
    }
}

