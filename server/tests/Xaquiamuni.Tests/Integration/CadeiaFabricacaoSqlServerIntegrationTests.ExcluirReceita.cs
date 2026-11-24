using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ExclusaoReceita_ValidaPrecondicoesPreservaNumeracaoERecuperaSucesso()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var primeira = await CriarReceitaAsync(admin, dados);
        var rota = $"/api/admin/receitas/{primeira.Resumo.Id:D}";
        var antiga = $"{rota}/versoes/{primeira.Atual.Id:D}";
        var etag = await VersaoProdutoAsync(admin, rota);
        var chave = Guid.NewGuid();
        using (var proibida = await RemoverInsumoAsync(cliente, rota, etag, chave))
            Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        using (var semCsrf = await RemoverInsumoAsync(admin, rota, etag, chave, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var semVersao = await RemoverInsumoAsync(admin, rota, null, chave))
            await ConferirErroPostalAsync(semVersao, 400, "versao_invalida");
        using (var ultima = await RemoverInsumoAsync(admin, antiga, etag, chave))
            await ConferirErroPostalAsync(ultima, 409, "ultima_versao_receita");
        using (var ausente = await RemoverInsumoAsync(admin, $"{rota}/versoes/{Guid.NewGuid():D}", etag, chave))
            Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);

        var segunda = await EditarReceitaParaExclusaoAsync(admin, primeira, 4);
        using (var desatualizada = await RemoverInsumoAsync(admin, antiga, etag, chave))
            await ConferirErroPostalAsync(desatualizada, 409, "versao_desatualizada");
        etag = await VersaoProdutoAsync(admin, rota);
        using (var atual = await RemoverInsumoAsync(admin, $"{rota}/versoes/{segunda.Atual.Id:D}", etag, chave))
            await ConferirErroPostalAsync(atual, 409, "versao_receita_atual");
        using (var excluida = await RemoverInsumoAsync(admin, antiga, etag, chave))
            Assert.Equal(HttpStatusCode.NoContent, excluida.StatusCode);
        using (var replay = await RemoverInsumoAsync(admin, antiga, etag, chave))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using (var incompatível = await RemoverInsumoAsync(admin, rota, etag, chave))
            await ConferirErroPostalAsync(incompatível, 409, "chave_em_uso");
        var restante = (await admin.Http.GetFromJsonAsync<AdminReceitasProducaoEndpoints.DetalheReceita>(rota))!;
        Assert.NotEqual(segunda.Token, restante.Token);
        Assert.Equal(2, Assert.Single(restante.Historico).Numero);
        Assert.Equal(3, (await EditarReceitaParaExclusaoAsync(admin, restante, 6)).Atual.Numero);

        var chaveIntegral = Guid.NewGuid();
        etag = await VersaoProdutoAsync(admin, rota);
        using (var excluida = await RemoverInsumoAsync(admin, rota, etag, chaveIntegral))
            Assert.Equal(HttpStatusCode.NoContent, excluida.StatusCode);
        var recadastrada = await CriarReceitaAsync(admin, dados);
        Assert.NotEqual(primeira.Resumo.Id, recadastrada.Resumo.Id);
        using (var repetida = await RemoverInsumoAsync(admin, rota, etag, chaveIntegral))
            Assert.Equal(HttpStatusCode.NoContent, repetida.StatusCode);
        Assert.Equal(recadastrada.Resumo.Id, await db.ReceitasProducao.Select(r => r.Id).SingleAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoReceita").SingleAsync());
        Assert.True(await db.InsumosEstoque.AnyAsync(i => i.Id == dados.Extrato));
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == dados.VarianteId));
    }

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_BloqueiaOrdemEmAndamento() => ConferirEstadoOrdemAsync("EmAndamento");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_BloqueiaOrdemCancelada() => ConferirEstadoOrdemAsync("Cancelada");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_BloqueiaOrdemConcluida() => ConferirEstadoOrdemAsync("Concluida");

    private static async Task ConferirEstadoOrdemAsync(string estado)
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var (id, ordem) = await ReceberEIniciarAsync(admin, dados);
        var ordemUrl = $"{Producao}/{ordem.Resumo.Id:D}";
        if (estado == "Cancelada") await admin.PostAsync<JsonElement>(ordemUrl + "/cancelar", new { }, HttpStatusCode.OK, Guid.NewGuid());
        if (estado == "Concluida")
            await admin.PostAsync<JsonElement>(ordemUrl + "/concluir", Conclusao(ordem, dados), HttpStatusCode.OK, Guid.NewGuid());
        var rota = $"/api/admin/receitas/{id:D}";
        var anterior = (await admin.Http.GetFromJsonAsync<AdminReceitasProducaoEndpoints.DetalheReceita>(rota))!;
        var nova = await EditarReceitaParaExclusaoAsync(admin, anterior, 4);
        var etag = await VersaoProdutoAsync(admin, rota);
        var movimentos = await db.MovimentosInsumo.CountAsync();
        var estoqueAcabado = await db.MovimentosProdutoAcabado.CountAsync();
        using (var usada = await RemoverInsumoAsync(admin, $"{rota}/versoes/{anterior.Atual.Id:D}", etag, Guid.NewGuid()))
            await ConferirErroPostalAsync(usada, 409, "versao_receita_com_producao");
        using (var bloqueada = await RemoverInsumoAsync(admin, rota, etag, Guid.NewGuid()))
            await ConferirErroPostalAsync(bloqueada, 409, "receita_com_producao");
        Assert.Equal(estado, (await db.OrdensProducao.Select(o => o.Situacao).SingleAsync()).ToString());
        Assert.Equal(nova.Token, await db.ReceitasProducao.Select(r => r.Token).SingleAsync());
        Assert.Equal(2, await db.Set<ReceitaProducaoVersao>().CountAsync());
        Assert.Equal(movimentos, await db.MovimentosInsumo.CountAsync());
        Assert.Equal(estoqueAcabado, await db.MovimentosProdutoAcabado.CountAsync());
    }

    private static async Task<AdminReceitasProducaoEndpoints.DetalheReceita> EditarReceitaParaExclusaoAsync(
        Sessao admin, AdminReceitasProducaoEndpoints.DetalheReceita atual, int rendimento)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/receitas/{atual.Resumo.Id:D}");
        request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(admin));
        request.Content = JsonContent.Create(new ReceitaInput(atual.Resumo.VarianteId, atual.Resumo.Nome, rendimento,
            atual.Atual.Itens.Select(i => new ItemReceitaInput(i.InsumoId, i.Quantidade, i.UnidadeBase)).ToArray(), atual.Token));
        using var resposta = await admin.Http.SendAsync(request);
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<AdminReceitasProducaoEndpoints.DetalheReceita>())!;
    }

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_ProducaoConfirmaPrimeiro() => DisputarReceitaAsync(true, "iniciar");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_ExclusaoConfirmaPrimeiro() => DisputarReceitaAsync(false, "iniciar");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_NovaVersaoConfirmaPrimeiro() => DisputarReceitaAsync(true, "editar");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_ExclusaoConfirmaAntesDaNovaVersao() => DisputarReceitaAsync(false, "editar");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_IntegralConfirmaAntesDaIndividual() => DisputarReceitaAsync(false, "excluir-versao");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_IndividualConfirmaAntesDaIntegral() => DisputarReceitaAsync(true, "excluir-versao");

    [SqlServerIntegrationFact]
    public Task ExclusaoReceita_DuasExclusoesGravamUmaAuditoria() => DisputarReceitaAsync(false, "excluir-receita");

    [SqlServerIntegrationFact]
    public async Task ExclusaoReceita_MigrationPreservaReceitaLegadaERollbackPreservaAgregado()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var migracao = db.GetService<IMigrator>();
        await migracao.MigrateAsync("20261124071353_AuditarExclusaoInsumos");
        var dados = await PrepararCompraAsync(admin, db);
        var primeira = await CriarReceitaAsync(admin, dados);
        var atual = await EditarReceitaParaExclusaoAsync(admin, primeira, 4);
        await migracao.MigrateAsync();

        var opcoes = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(ambiente.Banco.ConexaoIsolada)
            .AddInterceptors(new FalhaCommitVariante()).Options;
        await using var contextoComFalha = new XaquiamuniDbContext(opcoes);
        var autor = Guid.NewGuid();
        var chave = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfExcluirReceita(contextoComFalha, TimeProvider.System)
            .ExecutarAsync(atual.Resumo.Id, null, atual.Token, autor, chave, default));
        Assert.Equal(atual.Token, await db.ReceitasProducao.Select(r => r.Token).SingleAsync());
        Assert.Equal(2, await db.Set<ReceitaProducaoVersao>().CountAsync());
        Assert.Equal(6, await db.Set<ItemReceitaProducao>().CountAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoReceita").SingleAsync());

        var inativa = await new EfReceitaProducaoRepository(db, TimeProvider.System)
            .AlterarStatusAsync(atual.Resumo.Id, false, atual.Token, autor, default);
        Assert.Equal(ReceitaStatus.Sucesso, inativa.Status);
        Assert.Equal((204, null), await new EfExcluirReceita(db, TimeProvider.System)
            .ExecutarAsync(atual.Resumo.Id, null, inativa.Receita!.Token, autor, chave, default));
        Assert.Empty(await db.Set<ItemReceitaProducao>().ToArrayAsync());
    }

    private static async Task DisputarReceitaAsync(bool outraOperacaoPrimeiro, string operacao)
    {
        var barreira = new BarreiraCommitCatalogo();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var operador = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        if (operacao == "iniciar")
            await admin.PostAsync<JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        var receita = await CriarReceitaAsync(admin, dados);
        var id = receita.Resumo.Id;
        var rota = $"/api/admin/receitas/{id:D}";
        var anterior = receita.Atual.Id;
        if (operacao == "excluir-versao") receita = await EditarReceitaParaExclusaoAsync(admin, receita, 4);
        var etag = await VersaoProdutoAsync(admin, rota);
        Task<HttpResponseMessage> Excluir() => RemoverInsumoAsync(admin, rota, etag, Guid.NewGuid());
        async Task<HttpResponseMessage> Editar()
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, rota);
            request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(operador));
            request.Content = JsonContent.Create(new ReceitaInput(dados.VarianteId, receita.Resumo.Nome, 4,
                [new(dados.Extrato, "1", "Mililitro")], receita.Token));
            return await operador.Http.SendAsync(request);
        }
        Task<HttpResponseMessage> OutraOperacao() => operacao switch
        {
            "iniciar" => operador.EnviarAsync(Producao, new InicioProducaoInput(id, receita.Atual.Id, 4), Guid.NewGuid()),
            "editar" => Editar(),
            "excluir-versao" => RemoverInsumoAsync(operador, $"{rota}/versoes/{anterior:D}", etag, Guid.NewGuid()),
            _ => RemoverInsumoAsync(operador, rota, etag, Guid.NewGuid())
        };
        barreira.Armar();
        var primeira = outraOperacaoPrimeiro ? OutraOperacao() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiraPronta.Task.WaitAsync(TimeSpan.FromSeconds(15));
            segunda = outraOperacaoPrimeiro ? Excluir() : OutraOperacao();
            await barreira.SegundaIniciada.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var primeiraResposta = await primeira;
        using var segundaResposta = await segunda;
        var statusEsperado = outraOperacaoPrimeiro ? operacao switch
        {
            "iniciar" => HttpStatusCode.Created,
            "editar" => HttpStatusCode.OK,
            _ => HttpStatusCode.NoContent
        } : HttpStatusCode.NoContent;
        Assert.Equal(statusEsperado, primeiraResposta.StatusCode);
        if (outraOperacaoPrimeiro)
            await ConferirErroPostalAsync(segundaResposta, 409,
                operacao == "iniciar" ? "receita_com_producao" : "versao_desatualizada");
        else Assert.Equal(HttpStatusCode.NotFound, segundaResposta.StatusCode);
        Assert.Equal(outraOperacaoPrimeiro && operacao == "iniciar" ? 1 : 0, await db.OrdensProducao.CountAsync());
        Assert.Equal(outraOperacaoPrimeiro ? operacao == "editar" ? 2 : 1 : 0,
            await db.Set<ReceitaProducaoVersao>().CountAsync());
        Assert.Equal(!outraOperacaoPrimeiro || operacao == "excluir-versao" ? 1 : 0,
            await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoReceita").SingleAsync());
    }
}
