using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string Atendimento = "/api/admin/loja/atendimento";
    private const string ModalidadesSite = "/api/loja/modalidades-entrega";
    private const string ContatoSite = "/api/loja/contato";

    [SqlServerIntegrationFact]
    public async Task Atendimento_PadroesAutorizacaoCorreiosEWhatsApp()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var negado = await cliente.Http.GetAsync(Atendimento);
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        var contato = await cliente.Http.GetFromJsonAsync<JsonElement>(ContatoSite);
        Assert.Equal(JsonValueKind.Null, contato.GetProperty("whatsApp").ValueKind);
        Assert.Single(contato.EnumerateObject());
        var inicial = (await cliente.Http.GetFromJsonAsync<ModalidadesPublicas>(ModalidadesSite))!;
        Assert.Equal(new ModalidadesPublicas(true, false, false), inicial);
        Assert.Equal("0", inicial.FreteRetiradaCentavos);

        var modalidades = new SalvarModalidades(true, false, false, "Rua de teste", "Leve o número do pedido.", 0);
        await EscreverAtendimentoAsync(cliente, HttpMethod.Put, "/modalidades", modalidades, HttpStatusCode.Forbidden);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", modalidades, HttpStatusCode.BadRequest, csrf: false);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", modalidades, HttpStatusCode.BadRequest, idempotencia: false);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades",
            modalidades with { CorreiosHabilitados = true }, HttpStatusCode.BadRequest);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades",
            modalidades with { RetiradaHabilitada = false }, HttpStatusCode.Conflict);
        var configurado = await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", modalidades, HttpStatusCode.OK);
        Assert.Equal("Rua de teste", configurado.GetProperty("enderecoRetirada").GetString());
        Assert.Equal(1, configurado.GetProperty("revisao").GetInt32());

        var numero = new SalvarWhatsApp("+55 54 9256-0258", false, false, 1);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp", numero with { Numero = "+55x" }, HttpStatusCode.BadRequest);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp", numero with { Habilitado = true }, HttpStatusCode.BadRequest);
        var rascunho = await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp", numero, HttpStatusCode.OK);
        Assert.Equal("+555492560258", rascunho.GetProperty("whatsApp").GetString());
        Assert.Equal(JsonValueKind.Null, (await cliente.Http.GetFromJsonAsync<JsonElement>(ContatoSite)).GetProperty("whatsApp").ValueKind);

        var publicar = numero with { Confirmado = true, Habilitado = true, Revisao = 2 };
        var chave = Guid.NewGuid();
        var publicado = await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp", publicar, HttpStatusCode.OK, chave);
        Assert.Equal("+555492560258", (await cliente.Http.GetFromJsonAsync<JsonElement>(ContatoSite)).GetProperty("whatsApp").GetString());
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp",
            publicar with { Habilitado = false, Revisao = 3 }, HttpStatusCode.OK);
        var replay = await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp", publicar, HttpStatusCode.OK, chave);
        Assert.True(JsonElement.DeepEquals(publicado, replay));
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/whatsapp", publicar with { Habilitado = false }, HttpStatusCode.Conflict, chave);
        Assert.Equal(JsonValueKind.Null, (await cliente.Http.GetFromJsonAsync<JsonElement>(ContatoSite)).GetProperty("whatsApp").ValueKind);

        using var sair = await cliente.EnviarAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, sair.StatusCode);
        using var anonimo = await cliente.Http.GetAsync(Atendimento);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(inicial, await cliente.Http.GetFromJsonAsync<ModalidadesPublicas>(ModalidadesSite));
        await using var db = ambiente.Banco.CreateVerificationContext();
        Assert.Equal(4, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesAtendimento").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Atendimento_CidadesTarifaConcorrenciaEInvariantePreservamPedidoAntigo()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 2, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        var chaveCheckout = Guid.NewGuid();
        var carrinho = new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } } };
        var pedido = await cliente.PostAsync<JsonElement>("/api/pedidos", carrinho, HttpStatusCode.Created, chaveCheckout);
        var reserva = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        var movimento = await db.MovimentosProdutoAcabado.CountAsync();
        var caixaAntes = (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!.Itens;
        await db.Database.MigrateAsync("20261121231543_RegistrarComprovantesPix");
        await db.Database.MigrateAsync();

        var vacaria = new SalvarCidade("4322509", "Vacaria", "rs", 1250, false);
        await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", vacaria with { TarifaCentavos = -1 }, HttpStatusCode.BadRequest);
        await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", vacaria with { Uf = "SC" }, HttpStatusCode.BadRequest);
        var chaveCriacao = Guid.NewGuid();
        var criada = await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", vacaria, HttpStatusCode.Created, chaveCriacao);
        Assert.Equal("1250", criada.GetProperty("tarifaCentavos").GetString());
        Assert.Equal("RS", criada.GetProperty("uf").GetString());
        Assert.NotEqual(Guid.Empty, criada.GetProperty("criadoPor").GetGuid());
        Assert.True(JsonElement.DeepEquals(criada,
            await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", vacaria, HttpStatusCode.Created, chaveCriacao)));
        await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", vacaria, HttpStatusCode.Conflict);
        var id = criada.GetProperty("id").GetGuid();
        var rota = $"/cidades/{id:D}";
        var config = new SalvarModalidades(true, true, false, null, null, 0);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", config, HttpStatusCode.OK);
        Assert.False((await cliente.Http.GetFromJsonAsync<ModalidadesPublicas>(ModalidadesSite))!.EntregaPropria);

        var editar = vacaria with { Ativa = true, Revisao = 1, TarifaCentavos = 1300 };
        var token = (await admin.Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("requestToken").GetString();
        var corrida = await Task.WhenAll(EnviarAtendimentoAsync(admin, HttpMethod.Put, rota, editar, Guid.NewGuid(), token),
            EnviarAtendimentoAsync(admin, HttpMethod.Put, rota, editar with { TarifaCentavos = 1400 }, Guid.NewGuid(), token));
        using var a = corrida[0];
        using var b = corrida[1];
        Assert.Equal(new[] { 200, 409 }, corrida.Select(r => (int)r.StatusCode).Order().ToArray());
        var atual = await admin.Http.GetFromJsonAsync<JsonElement>(Atendimento + rota);
        Assert.Contains(atual.GetProperty("tarifaCentavos").GetString(), new[] { "1300", "1400" });
        Assert.Equal(2, atual.GetProperty("revisao").GetInt32());
        Assert.True((await cliente.Http.GetFromJsonAsync<ModalidadesPublicas>(ModalidadesSite))!.EntregaPropria);

        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", config with { RetiradaHabilitada = false, Revisao = 1 }, HttpStatusCode.OK);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, rota, editar with { Ativa = false, Revisao = 2 }, HttpStatusCode.Conflict);
        await EscreverAtendimentoAsync(admin, HttpMethod.Delete, rota + "?revisao=2", null, HttpStatusCode.Conflict);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", config with { Revisao = 2 }, HttpStatusCode.OK);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, rota, editar with { Ativa = false, Revisao = 2 }, HttpStatusCode.OK);
        Assert.False((await cliente.Http.GetFromJsonAsync<ModalidadesPublicas>(ModalidadesSite))!.EntregaPropria);
        await EscreverAtendimentoAsync(admin, HttpMethod.Delete, rota + "?revisao=3", null, HttpStatusCode.OK);
        using var removida = await admin.Http.GetAsync(Atendimento + rota);
        Assert.Equal(HttpStatusCode.NotFound, removida.StatusCode);
        Assert.Empty(await admin.Http.GetFromJsonAsync<CidadeEntregaDto[]>(Atendimento + "/cidades") ?? throw new InvalidOperationException());

        var replay = await cliente.PostAsync<JsonElement>("/api/pedidos", carrinho, HttpStatusCode.OK, chaveCheckout);
        Assert.True(JsonElement.DeepEquals(pedido, replay));
        Assert.Equal("RetiradaLocal", replay.GetProperty("modalidadeEntrega").GetString());
        var depois = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal((reserva.Id, reserva.Estado, reserva.Quantidade, reserva.ExpiraEmUtc),
            (depois.Id, depois.Estado, depois.Quantidade, depois.ExpiraEmUtc));
        Assert.Equal(movimento, await db.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(caixaAntes, (await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!.Itens);
        Assert.Equal(4, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM OperacoesAtendimento WHERE CidadeId = {id}").SingleAsync());
    }

    private static async Task<JsonElement> EscreverAtendimentoAsync(Sessao sessao, HttpMethod metodo, string rota,
        object? dados, HttpStatusCode esperado, Guid? chave = null, bool csrf = true, bool idempotencia = true)
    {
        var token = csrf
            ? (await sessao.Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("requestToken").GetString()
            : null;
        using var resposta = await EnviarAtendimentoAsync(sessao, metodo, rota, dados,
            idempotencia ? chave ?? Guid.NewGuid() : null, token);
        Assert.True(resposta.StatusCode == esperado,
            $"{rota}: esperado {(int)esperado}, recebido {(int)resposta.StatusCode}.");
        return resposta.IsSuccessStatusCode ? await resposta.Content.ReadFromJsonAsync<JsonElement>() : default;
    }

    private static async Task<HttpResponseMessage> EnviarAtendimentoAsync(Sessao sessao, HttpMethod metodo, string rota,
        object? dados, Guid? chave, string? token)
    {
        using var request = new HttpRequestMessage(metodo, Atendimento + rota);
        if (dados is not null) request.Content = JsonContent.Create(dados);
        if (chave is { } id) request.Headers.Add("Idempotency-Key", id.ToString("D"));
        if (token is not null) request.Headers.Add("X-XSRF-TOKEN", token);
        return await sessao.Http.SendAsync(request);
    }
}
