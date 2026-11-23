using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string Postal = "/api/cotacoes-correios";
    private const string PostalAdmin = "/api/admin/cotacoes-correios";
    private static readonly Dictionary<string, MunicipioPostal> MunicipiosPostaisTeste =
        new() { ["95200000"] = new("4322509", "Vacaria", "RS") };
    private static readonly EnderecoCotacaoEntrega DestinoPostalTeste =
        new("95200-000", "rs", "Vacaria", "Centro", "Rua de teste", "12", null);

    [SqlServerIntegrationFact]
    public async Task CorreiosManual_ProtegeTitularidadeAuditaRevisaoEAceiteSemPedido()
    {
        await using var ambiente = await Ambiente.CriarAsync(municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 2 } };
        var pedidoCotacao = new { itens, endereco = DestinoPostalTeste };

        using (var desabilitada = await EscreverPostalAsync(cliente, Postal, pedidoCotacao))
            await ConferirErroPostalAsync(desabilitada, 409, "cotacao_manual_desabilitada");
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 0, 0), HttpStatusCode.BadRequest);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 48, 0), HttpStatusCode.OK);
        var modalidade = await cliente.Http.GetFromJsonAsync<ModalidadesPublicas>(ModalidadesSite);
        Assert.Equal(new ModalidadesPublicas(true, false, false, true), modalidade);
        Assert.Equal("0", modalidade!.FreteRetiradaCentavos);
        using (var semCsrf = await EscreverPostalAsync(cliente, Postal, pedidoCotacao, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var invalido = await EscreverPostalAsync(cliente, Postal,
            pedidoCotacao with { itens = [itens[0] with { quantidade = 0 }] }))
            await ConferirErroPostalAsync(invalido, 400, "itens_invalidos");
        foreach (var endereco in new[] { DestinoPostalTeste with { Numero = "" },
            DestinoPostalTeste with { Cidade = "Outra cidade" }, DestinoPostalTeste with { Cep = "90000000" } })
        {
            using var invalido = await EscreverPostalAsync(cliente, Postal, pedidoCotacao with { endereco = endereco });
            await ConferirErroPostalAsync(invalido, 400, "destino_nao_validado");
        }
        var chave = Guid.NewGuid();
        using var criada = await EscreverPostalAsync(cliente, Postal, pedidoCotacao, chave: chave);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var original = await criada.Content.ReadFromJsonAsync<JsonElement>();
        var id = original.GetProperty("id").GetGuid();
        var rota = $"{Postal}/{id:D}";
        var rotaAdmin = $"{PostalAdmin}/{id:D}";
        using (var duplicada = await EscreverPostalAsync(cliente, Postal, pedidoCotacao))
            await ConferirErroPostalAsync(duplicada, 409, "solicitacao_duplicada");
        using (var conflito = await EscreverPostalAsync(cliente, Postal,
            pedidoCotacao with { endereco = DestinoPostalTeste with { Numero = "15" } }, chave: chave))
            await ConferirErroPostalAsync(conflito, 409, "chave_em_uso");
        using var alheia = await outro.Http.GetAsync(rota);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        using var proibida = await cliente.Http.GetAsync(rotaAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        using (var alteracaoAlheia = await EscreverPostalAsync(outro, rota + "/cancelar", new { }, "\"1\""))
            Assert.Equal(HttpStatusCode.NotFound, alteracaoAlheia.StatusCode);
        using var detalhe = await cliente.Http.GetAsync(rota);
        Assert.True(detalhe.Headers.CacheControl?.NoStore);
        Assert.Equal("\"1\"", detalhe.Headers.ETag!.Tag);
        var congelada = (await detalhe.Content.ReadFromJsonAsync<DetalheCotacaoCorreios>())!;
        Assert.Equal("4322509", congelada.Endereco.CodigoIbge);
        Assert.Equal(2, Assert.Single(congelada.Itens).Quantidade);

        var proposta = new PublicarPropostaCorreios("PAC", 1900, 5, null, 600,
            [new(200, 12, 13, 14), new(400, null, null, null)]);
        using (var inconsistente = await EscreverPostalAsync(admin, rotaAdmin + "/propostas",
            proposta with { PesoTotalGramas = 1 }, "\"1\""))
            await ConferirErroPostalAsync(inconsistente, 400, "proposta_invalida");
        using var primeira = await EscreverPostalAsync(admin, rotaAdmin + "/propostas", proposta, "\"1\"");
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        using var segunda = await EscreverPostalAsync(admin, rotaAdmin + "/propostas", proposta with { FreteCentavos = 2100 }, "\"2\"");
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        using (var antiga = await EscreverPostalAsync(cliente, rota + "/aceitar", new { revisao = 1 }, "\"3\""))
            await ConferirErroPostalAsync(antiga, 409, "revisao_desatualizada");
        var chaveAceite = Guid.NewGuid();
        using var aceita = await EscreverPostalAsync(cliente, rota + "/aceitar", new { revisao = 2 }, "\"3\"", chaveAceite);
        Assert.Equal(HttpStatusCode.OK, aceita.StatusCode);
        using (var revisao = await EscreverPostalAsync(admin, rotaAdmin + "/propostas", proposta, "\"4\""))
            await ConferirErroPostalAsync(revisao, 409, "estado_incompativel");
        using var cancelada = await EscreverPostalAsync(cliente, rota + "/cancelar", new { motivo = "Desistência" }, "\"4\"");
        Assert.Equal(HttpStatusCode.OK, cancelada.StatusCode);
        using var replay = await EscreverPostalAsync(cliente, rota + "/aceitar", new { revisao = 2 }, "\"3\"", chaveAceite);
        Assert.Equal(await aceita.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var replayCriacao = await EscreverPostalAsync(cliente, Postal, pedidoCotacao, chave: chave);
        Assert.Equal(original.GetRawText(), (await replayCriacao.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        var atual = (await admin.Http.GetFromJsonAsync<DetalheCotacaoCorreios>(rotaAdmin))!;
        Assert.Equal(2, atual.RevisaoAceita);
        Assert.Equal(new long[] { 2100, 1900 }, atual.Propostas.Select(x => x.FreteCentavos));
        Assert.All(atual.Propostas, x => Assert.Equal(TimeSpan.FromHours(48), x.ValidaAteUtc - x.PublicadaEmUtc));
        Assert.Equal(2, atual.Propostas[1].Volumes.Count);
        Assert.Equal(5, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesCotacoesCorreios").SingleAsync());
        Assert.False(await db.Pedidos.AnyAsync());
        Assert.False(await db.ReservasProdutoAcabado.AnyAsync());
        Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());
        Assert.Empty((await admin.Http.GetFromJsonAsync<JsonElement>("/api/admin/financeiro/movimentacoes"))
            .GetProperty("itens").EnumerateArray());
        using var pix = await cliente.Http.GetAsync($"/api/pedidos/{id:D}/pagamento");
        Assert.Equal(HttpStatusCode.NotFound, pix.StatusCode);
        using var sair = await outro.EnviarAsync("/api/auth/logout", new { });
        using var anonimo = await outro.Http.GetAsync(Postal);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task CorreiosManual_ExpiracaoRecusaConcorrenciaELimitesPorCliente()
    {
        var relogio = new RelogioPostalTeste();
        await using var ambiente = await Ambiente.CriarAsync(relogio: relogio, municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 1, 0), HttpStatusCode.OK);
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        object Nova(int quantidade) => new
        {
            itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade } },
            endereco = DestinoPostalTeste,
        };
        async Task<Guid> CriarAsync(int quantidade)
        {
            using var resposta = await EscreverPostalAsync(cliente, Postal, Nova(quantidade));
            Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
            return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var id1 = await CriarAsync(1);
        var id2 = await CriarAsync(2);
        var id3 = await CriarAsync(3);
        using (var limite = await EscreverPostalAsync(cliente, Postal, Nova(4)))
            await ConferirErroPostalAsync(limite, 429, "limite_solicitacoes_abertas");
        var proposta = new PublicarPropostaCorreios("SEDEX", 1900, 2, relogio.GetUtcNow().AddMinutes(1), 200, null);
        var rota1 = $"{Postal}/{id1:D}";
        var tarefas = new List<HttpResponseMessage>();
        using var publicada = await EscreverPostalAsync(admin, $"{PostalAdmin}/{id1:D}/propostas", proposta, "\"1\"");
        Assert.Equal(HttpStatusCode.OK, publicada.StatusCode);
        tarefas.AddRange(await Task.WhenAll(
            EscreverPostalAsync(admin, $"{PostalAdmin}/{id1:D}/propostas", proposta with { FreteCentavos = 2400 }, "\"2\""),
            EscreverPostalAsync(cliente, rota1 + "/aceitar", new { revisao = 1 }, "\"2\"")));
        try
        {
            Assert.Single(tarefas, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(tarefas, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var resposta in tarefas) resposta.Dispose(); }
        relogio.Agora = proposta.ValidaAteUtc!.Value;
        var pagina = (await admin.Http.GetFromJsonAsync<PaginaCotacoesCorreios>($"{PostalAdmin}?situacao=Expirada&itensPorPagina=1"))!;
        Assert.Equal(id1, Assert.Single(pagina.Itens).Id);
        using (var vencida = await EscreverPostalAsync(cliente, rota1 + "/aceitar", new { revisao = 2 }, "\"3\""))
            await ConferirErroPostalAsync(vencida, 409, "proposta_expirada");
        using var recusada = await EscreverPostalAsync(admin, $"{PostalAdmin}/{id2:D}/recusar", new { motivo = "Teste" }, "\"1\"");
        Assert.Equal(HttpStatusCode.OK, recusada.StatusCode);
        using var cancelada = await EscreverPostalAsync(cliente, $"{Postal}/{id3:D}/cancelar", new { }, "\"1\"");
        Assert.Equal(HttpStatusCode.OK, cancelada.StatusCode);
        await CriarAsync(4);
        await CriarAsync(5);
        using (var frequencia = await EscreverPostalAsync(cliente, Postal, Nova(6)))
            await ConferirErroPostalAsync(frequencia, 429, "limite_frequencia");
        relogio.Agora = relogio.Agora.AddHours(1);
        await CriarAsync(6);
    }

    private sealed class RelogioPostalTeste : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private static async Task<HttpResponseMessage> EscreverPostalAsync(Sessao sessao, string rota, object corpo,
        string? versao = null, Guid? chave = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, rota) { Content = JsonContent.Create(corpo) };
        if (csrf) request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
        if (versao is not null) request.Headers.Add("If-Match", versao);
        request.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString("D"));
        return await sessao.Http.SendAsync(request);
    }

    private static async Task ConferirErroPostalAsync(HttpResponseMessage resposta, int status, string codigo)
    {
        Assert.Equal(status, (int)resposta.StatusCode);
        Assert.Equal(codigo, (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
    }
}
