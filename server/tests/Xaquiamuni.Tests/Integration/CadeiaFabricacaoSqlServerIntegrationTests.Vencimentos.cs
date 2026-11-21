using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string ContasAbertas = Financeiro + "/contas-em-aberto";

    [SqlServerIntegrationFact]
    public async Task Vencimentos_ConciliaOrigensEFiltrosAoCruzarMeiaNoiteEAnoNovo()
    {
        var relogio = new RelogioVencimentos { Agora = new DateTimeOffset(2026, 12, 31, 2, 59, 59, TimeSpan.Zero) };
        await using var ambiente = await Ambiente.CriarAsync(relogio: relogio);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var negada = await cliente.Http.GetAsync(ContasAbertas);
        Assert.Equal(HttpStatusCode.Forbidden, negada.StatusCode);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db, custoParcial: true);
        var hoje = new DateOnly(2026, 12, 31);
        var compra = await admin.PostAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>(Compras,
            new CompraMateriaPrimaInput(dados.FornecedorId, hoje,
                [new(null, 1, 10.03m, [new(dados.Extrato, null, 1, null)])], null),
            HttpStatusCode.Created, Guid.NewGuid());
        await admin.PostAsync<PagamentoFornecedorDto>(Financeiro + $"/compras/{compra.Resumo.Id:D}/pagamentos",
            new PagarFornecedorInput(hoje.AddDays(-1), "Pix", null, 501), HttpStatusCode.OK, Guid.NewGuid());

        var variante = await db.ProdutoVariantes.Include(v => v.Produto).SingleAsync(v => v.Id == dados.VarianteId);
        var autor = Guid.NewGuid();
        VendaExterna Venda(DateOnly? vencimento, long valor) => VendaExterna.Criar(CanalVendaExterna.Presencial,
            null, hoje.AddDays(-2), "Cliente sintético", vencimento, [new ItemVendaExterna(variante, 1, valor, 0)], autor, relogio.Agora);
        var vencida = Venda(hoje.AddDays(-1), 1000);
        vencida.Receber(101, hoje.AddDays(-1), MeioRecebimentoVendaExterna.Dinheiro, autor, Guid.NewGuid(), relogio.Agora);
        var semData = Venda(null, 200);
        var paga = Venda(hoje, 900);
        paga.Receber(900, hoje.AddDays(-1), MeioRecebimentoVendaExterna.Dinheiro, autor, Guid.NewGuid(), relogio.Agora);
        var cancelada = Venda(hoje, 800);
        cancelada.Cancelar("Teste", autor, relogio.Agora);
        db.AddRange(vencida, semData, paga, cancelada);
        await db.SaveChangesAsync();

        Task<LancamentoDto> Criar(string tipo, long valor, int dias) => admin.PostAsync<LancamentoDto>(Pendencias,
            new CriarLancamentoPendenteInput(tipo, "Teste", "Conta manual", hoje.AddDays(dias), valor, "Contraparte"),
            HttpStatusCode.Created, Guid.NewGuid());
        var manualAtrasada = await Criar("Saida", 100, -1);
        var manualHoje = await Criar("Saida", 300, 0);
        var manualTres = await Criar("Entrada", 400, 3);
        var manualQuatro = await Criar("Saida", 500, 4);

        var antes = (await ObterResumoAsync(admin, "")).PosicaoAtual.ContasEmAberto;
        Assert.Equal(hoje.AddDays(-1), antes.DataOperacional);
        Assert.Equal(new TotalContasDto(1, "899"), antes.AReceber.VenceHoje);
        relogio.Agora = relogio.Agora.AddSeconds(1);
        var resumo = await ObterResumoAsync(admin, "");
        var contas = resumo.PosicaoAtual.ContasEmAberto;
        Assert.Equal(hoje, contas.DataOperacional);
        Assert.Equal(new IndicadoresDirecaoContaDto(4, "1402", new(1, "100"), new(1, "300"), new(1, "300")), contas.APagar);
        Assert.Equal(new IndicadoresDirecaoContaDto(3, "1499", new(1, "899"), new(0, "0"), new(1, "400")), contas.AReceber);
        Assert.Equal(("1402", "1499", 1L), (resumo.PosicaoAtual.APagarCentavos,
            resumo.PosicaoAtual.AReceberCentavos, resumo.PosicaoAtual.ComprasValorAConferir));
        Assert.Equal(contas, (await ObterResumoAsync(admin, "?dataInicial=2027-02-01")).PosicaoAtual.ContasEmAberto);

        async Task<PaginaContasEmAberto> Listar(string filtros) =>
            (await admin.Http.GetFromJsonAsync<PaginaContasEmAberto>(ContasAbertas + filtros))!;
        var todas = await Listar("");
        Assert.Equal(hoje, todas.DataOperacional);
        Assert.Equal(7, todas.Itens.Select(c => (c.Origem, c.Id)).Distinct().Count());
        Assert.All(todas.Itens.TakeLast(2), item => Assert.Null(item.Vencimento));
        var pendenciaCompra = Assert.Single(todas.Itens, item => item.Origem == "Compra");
        Assert.Equal(("502", "EmAberto", compra.Resumo.Id), (pendenciaCompra.ValorPendenteCentavos,
            pendenciaCompra.Situacao, pendenciaCompra.Id));
        Assert.Equal($"{Financeiro}/compras/{compra.Resumo.Id:D}",
            pendenciaCompra.ReferenciaApi);
        Assert.Equal("EmAberto", Assert.Single(todas.Itens, item => item.Id == semData.Id).Situacao);
        Assert.Equal("EmAberto", Assert.Single(todas.Itens, item => item.Id == manualQuatro.Id).Situacao);
        Assert.Equal(manualAtrasada.Id, Assert.Single((await Listar("?direcao=APagar&classificacao=Vencidas")).Itens).Id);
        Assert.Equal(vencida.Id, Assert.Single((await Listar("?origem=VendaExterna&classificacao=Vencidas")).Itens).Id);
        Assert.Equal(manualTres.Id, Assert.Single((await Listar("?direcao=AReceber&classificacao=VencendoEmBreve")).Itens).Id);
        Assert.Equal("VenceHoje", Assert.Single((await Listar("?direcao=APagar&classificacao=VencendoEmBreve")).Itens).Situacao);

        var dataEfetiva = hoje.AddDays(-3);
        await admin.PostAsync<LancamentoDto>($"{Lancamentos}/{manualHoje.Id:D}/quitar", new QuitarLancamentoInput(dataEfetiva, "Pix"),
            HttpStatusCode.OK, Guid.NewGuid());
        await admin.PostAsync<LancamentoDto>($"{Lancamentos}/{manualTres.Id:D}/cancelar", new CancelarLancamentoInput("Teste"),
            HttpStatusCode.OK, Guid.NewGuid());
        var depois = await ObterResumoAsync(admin, $"?dataInicial={dataEfetiva:yyyy-MM-dd}&dataFinal={dataEfetiva:yyyy-MM-dd}");
        Assert.Equal("300", depois.FluxoPeriodo.SaidasPagasCentavos);
        Assert.Equal(("1102", "1099"), (depois.PosicaoAtual.APagarCentavos, depois.PosicaoAtual.AReceberCentavos));
        Assert.Empty((await Listar("?classificacao=VencendoEmBreve")).Itens);
        Assert.Equal(new TotalContasDto(0, "0"), depois.PosicaoAtual.ContasEmAberto.AReceber.VencendoEmBreve);

        await admin.PostAsync<PagamentoFornecedorDto>(Financeiro + $"/compras/{compra.Resumo.Id:D}/pagamentos",
            new PagarFornecedorInput(dataEfetiva, "Pix", null, 502), HttpStatusCode.OK, Guid.NewGuid());
        await admin.PostAsync<ResumoVendaExterna>($"/api/admin/vendas-externas/{vencida.Id:D}/recebimentos",
            new ReceberVendaExternaInput(899, hoje, "Dinheiro"), HttpStatusCode.OK, Guid.NewGuid());
        var final = (await ObterResumoAsync(admin, "")).PosicaoAtual.ContasEmAberto;
        Assert.Equal((2L, "600", 1L, "200"), (final.APagar.Quantidade, final.APagar.ValorCentavos,
            final.AReceber.Quantidade, final.AReceber.ValorCentavos));
        Assert.Empty((await Listar("?origem=VendaExterna&classificacao=Vencidas")).Itens);
    }

    [SqlServerIntegrationFact]
    public async Task Vencimentos_PaginacaoEstavelCentavosAcimaDeBigintEQuantidadeTotal()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var data = new DateOnly(2027, 1, 1);
        var agora = DateTimeOffset.UtcNow;
        var autor = Guid.NewGuid();
        var registros = Enumerable.Range(1, 51).Select(n => LancamentoFinanceiro.CriarPendente(
            TipoLancamento.Saida, "Teste", $"Conta {n}", long.MaxValue, data, autor, agora, null, null)).ToArray();
        db.AddRange(registros);
        await db.SaveChangesAsync();

        const string filtro = "?origem=Manual&direcao=APagar";
        var primeira = (await admin.Http.GetFromJsonAsync<PaginaContasEmAberto>(ContasAbertas + filtro))!;
        var segunda = (await admin.Http.GetFromJsonAsync<PaginaContasEmAberto>(ContasAbertas + filtro + "&pagina=2"))!;
        Assert.Equal(50, primeira.Itens.Count);
        Assert.True(primeira.TemProxima);
        Assert.False(segunda.TemProxima);
        Assert.Equal(51, primeira.Itens.Append(Assert.Single(segunda.Itens)).Select(c => c.Id).Distinct().Count());
        Assert.All(primeira.Itens, item => Assert.Equal("9223372036854775807", item.ValorPendenteCentavos));
        var totais = (await ObterResumoAsync(admin, "")).PosicaoAtual.ContasEmAberto;
        Assert.Equal((51L, "470391973879593566157"), (totais.APagar.Quantidade, totais.APagar.ValorCentavos));
        using var invalido = await admin.Http.GetAsync(ContasAbertas + "?classificacao=1");
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    private sealed class RelogioVencimentos : TimeProvider
    {
        public DateTimeOffset Agora { get; set; }
        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
