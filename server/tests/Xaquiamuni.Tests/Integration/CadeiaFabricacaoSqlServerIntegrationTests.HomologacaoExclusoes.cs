using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task HomologacaoExclusoes_RedefinirPoliticaPreservaLoteEMovimento()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (produto, _, variante) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var calendario = banco.Services.GetRequiredService<ICalendarioOperacionalEstoque>();
        var estoque = new EfEstoqueProdutoAcabadoRepository(db, TimeProvider.System, calendario);
        var autor = Guid.NewGuid();
        Assert.True((await estoque.ConfigurarPoliticaAsync(new(variante.Id, 3, 45))).Sucesso);
        var entrada = await estoque.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            variante.Id, autor, TipoMovimentoProdutoAcabado.EntradaManual, 2, CondicaoEstoqueProdutoAcabado.Vendavel,
            "Entrada de homologação", "entrada-politica",
            DataFabricacaoNovoLote: calendario.ObterDataAtual(),
            DataValidadeNovoLote: calendario.ObterDataAtual().AddDays(45)));
        Assert.True(entrada.Sucesso, entrada.Erro);
        var lote = entrada.Lote!;
        var movimento = entrada.Movimento!;
        Assert.True((await estoque.ConfigurarPoliticaAsync(new(variante.Id, null, null,
            ConfigurarPoliticaProdutoAcabadoInput.Precondicao(3, 45)))).Sucesso);
        var preservado = await db.LotesProdutoAcabado.AsNoTracking().SingleAsync(l => l.Id == lote.Id);
        Assert.Equal(lote.DataValidade, preservado.DataValidade);
        Assert.Equal(lote.PrazoValidadeAplicadoDias, preservado.PrazoValidadeAplicadoDias);
        Assert.True(await db.MovimentosProdutoAcabado.AnyAsync(m => m.Id == movimento.Id));
        Assert.False(await db.PoliticasEstoqueProdutoVariante.AnyAsync(p => p.ProdutoVarianteId == variante.Id));
        Assert.Equal("variante_com_historico_estoque",
            await new DependenciasExclusaoCatalogo(db).ConsultarAsync(produto.Id, variante.Id, default));
    }

    [SqlServerIntegrationFact]
    public Task HomologacaoExclusoes_ReceitaEInsumoEmTransacoesIndependentes() => DisputarReceitaComCadastroAsync(false);

    [SqlServerIntegrationFact]
    public Task HomologacaoExclusoes_ReceitaEVarianteEmTransacoesIndependentes() => DisputarReceitaComCadastroAsync(true);

    [SqlServerIntegrationFact]
    public Task HomologacaoExclusoes_AtualizacaoDaPoliticaPrecedeRedefinicao() => DisputarPoliticaAsync(true);

    [SqlServerIntegrationFact]
    public Task HomologacaoExclusoes_RedefinicaoDaPoliticaPrecedeExclusao() => DisputarPoliticaAsync(false);

    private static async Task DisputarPoliticaAsync(bool atualizacaoPrimeiro)
    {
        var barreira = new BarreiraCommitCatalogo();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, variante) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var politica = $"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/politica";
        var versao = await VersaoProdutoAsync(outro, rota);
        async Task<HttpResponseMessage> Gravar(Sessao sessao, object corpo)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, politica) { Content = JsonContent.Create(corpo) };
            request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
            return await sessao.Http.SendAsync(request);
        }
        using var inicial = await Gravar(admin, new { estoqueMinimo = 2 });
        Assert.Equal(HttpStatusCode.OK, inicial.StatusCode);
        var redefinicao = new
        {
            redefinirSeCorresponder = (await inicial.Content.ReadFromJsonAsync<PoliticaProdutoAcabadoAdminResponse>())!.Precondicao
        };

        barreira.Armar();
        var primeira = atualizacaoPrimeiro ? Gravar(admin, new { estoqueMinimo = 8 }) : Gravar(admin, redefinicao);
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiraPronta.Task.WaitAsync(TimeSpan.FromSeconds(15));
            segunda = atualizacaoPrimeiro ? Gravar(outro, redefinicao)
                : ExcluirVarianteAsync(outro, rota, variante.Id, versao, Guid.NewGuid());
            await barreira.SegundaIniciada.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var resultadoPrimeiro = await primeira;
        using var resultadoSegundo = await segunda;
        Assert.Equal(HttpStatusCode.OK, resultadoPrimeiro.StatusCode);
        Assert.Equal(atualizacaoPrimeiro ? HttpStatusCode.Conflict : HttpStatusCode.NoContent, resultadoSegundo.StatusCode);
        Assert.Equal(atualizacaoPrimeiro, await db.ProdutoVariantes.AnyAsync(v => v.Id == variante.Id));
        var minima = await db.PoliticasEstoqueProdutoVariante.Select(p => p.EstoqueMinimo).SingleOrDefaultAsync();
        Assert.Equal(atualizacaoPrimeiro ? 8 : (int?)null, minima);
    }

    private static async Task DisputarReceitaComCadastroAsync(bool excluirVariante)
    {
        var barreira = new BarreiraCommitCatalogo();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, padrao, variante) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var material = InsumoEstoque.Criar("Material livre", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.InsumosEstoque.Add(material);
        await db.SaveChangesAsync();
        var receita = await admin.PostAsync<AdminReceitasProducaoEndpoints.DetalheReceita>("/api/admin/receitas",
            new ReceitaInput(variante.Id, "Receita livre", 1, [new(material.Id, "1", "Grama")], null), HttpStatusCode.Created);
        var rotaReceita = $"/api/admin/receitas/{receita.Resumo.Id:D}";
        var rota = excluirVariante ? $"/api/admin/produtos/{produto.Id:D}" : $"/api/admin/insumos/{material.Id:D}";
        var versao = await VersaoProdutoAsync(outro, rota);
        var versaoReceita = await VersaoProdutoAsync(admin, rotaReceita);
        Task<HttpResponseMessage> ExcluirCadastro() => excluirVariante
            ? ExcluirVarianteAsync(outro, rota, variante.Id, versao, Guid.NewGuid())
            : RemoverInsumoAsync(outro, rota, versao, Guid.NewGuid());
        using (var bloqueado = await ExcluirCadastro())
            await ConferirErroPostalAsync(bloqueado, 409,
                excluirVariante ? "variante_com_historico_producao" : "insumo_com_receitas");

        barreira.Armar();
        var primeira = RemoverInsumoAsync(admin, rotaReceita, versaoReceita, Guid.NewGuid());
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiraPronta.Task.WaitAsync(TimeSpan.FromSeconds(15));
            segunda = ExcluirCadastro();
            await barreira.SegundaIniciada.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var removida = await primeira;
        using var cadastro = await segunda;
        Assert.Equal(HttpStatusCode.NoContent, removida.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cadastro.StatusCode);
        Assert.False(await db.Set<ItemReceitaProducao>().AnyAsync(i => i.InsumoId == material.Id));
        Assert.Equal(!excluirVariante, await db.ProdutoVariantes.AnyAsync(v => v.Id == variante.Id));
        Assert.Equal(excluirVariante, await db.InsumosEstoque.AnyAsync(i => i.Id == material.Id));
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == padrao.Id && v.Padrao));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoReceita").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task HomologacaoExclusoes_RedefinirPoliticaPermiteExcluirVarianteSemHistorico()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, padrao, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var politica = $"/api/admin/estoque/produtos-acabados/variantes/{extra.Id:D}/politica";
        async Task<HttpResponseMessage> Alterar(Sessao sessao, object corpo, bool csrf = true, string? destino = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, destino ?? politica) { Content = JsonContent.Create(corpo) };
            if (csrf) request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
            return await sessao.Http.SendAsync(request);
        }
        using var outra = await Alterar(admin, new { estoqueMinimo = 8 },
            destino: $"/api/admin/estoque/produtos-acabados/variantes/{padrao.Id:D}/politica");
        Assert.Equal(HttpStatusCode.OK, outra.StatusCode);
        using var criada = await Alterar(admin, new { estoqueMinimo = 3, prazoValidadePadraoDias = 45 });
        var tag = (await criada.Content.ReadFromJsonAsync<PoliticaProdutoAcabadoAdminResponse>())!.Precondicao;
        Assert.NotNull(tag);
        var versao = await VersaoProdutoAsync(admin, rota);
        using (var bloqueada = await ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid()))
            await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_estoque");
        using var vazia = await Alterar(admin, new { });
        var tagVazia = (await vazia.Content.ReadFromJsonAsync<PoliticaProdutoAcabadoAdminResponse>())!.Precondicao;
        Assert.NotNull(tagVazia);
        using (var bloqueada = await ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid()))
            await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_estoque");
        using (var conflito = await Alterar(admin, new { redefinirSeCorresponder = tag }))
            Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        var redefinicao = new { redefinirSeCorresponder = tagVazia };
        using (var proibida = await Alterar(cliente, redefinicao))
            Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        using (var semCsrf = await Alterar(admin, redefinicao, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var ok = await Alterar(admin, redefinicao))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var replay = await Alterar(admin, redefinicao))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var excluida = await ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NoContent, excluida.StatusCode);
        Assert.Equal(padrao.Id, await db.PoliticasEstoqueProdutoVariante.Select(p => p.ProdutoVarianteId).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task HomologacaoExclusoes_PreviaAntigaFalhaComVersaoPresenteOuRemovida()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var primeira = await CriarReceitaAsync(admin, dados);
        var id = primeira.Resumo.Id;
        var previa = await PreviaAsync(admin, id);
        await EditarReceitaParaExclusaoAsync(admin, primeira, 4);
        var inicio = new InicioProducaoInput(id, previa.ReceitaVersaoId, 4);
        using (var desatualizada = await admin.EnviarAsync(Producao, inicio, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, desatualizada.StatusCode);
        var rota = $"/api/admin/receitas/{id:D}";
        using (var exclusao = await RemoverInsumoAsync(admin, $"{rota}/versoes/{previa.ReceitaVersaoId:D}",
            await VersaoProdutoAsync(admin, rota), Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NoContent, exclusao.StatusCode);
        using (var desatualizada = await admin.EnviarAsync(Producao, inicio, Guid.NewGuid()))
        {
            Assert.Equal(HttpStatusCode.Conflict, desatualizada.StatusCode);
            Assert.Contains("Consulte uma nova prévia", await desatualizada.Content.ReadAsStringAsync());
        }
        Assert.False(await db.OrdensProducao.AnyAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync());
        Assert.False(await db.MovimentosInsumo.AnyAsync());
        Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());
    }
}
