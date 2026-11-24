using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    private const string PromocoesUrl = "/api/admin/promocoes";

    [SqlServerIntegrationFact]
    public async Task Promocoes_AdminPreviaReplayEdicaoEPrecosPublicosIntactos()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261121005851_AdicionarPublicacaoTemaLoja");
        await using var db = banco.CreateVerificationContext();
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(db, categoriaAnteriorAVersao: true);
        await db.Database.MigrateAsync();
        await EntrarAsync(db, padrao.Id, 1, DateOnly.FromDateTime((await LerUtcSqlAsync(db)).UtcDateTime).AddDays(10));
        var clienteId = await CriarClienteCompraAsync(db);
        using var factory = CriarApiPedidos(banco);
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        using var cliente = ClienteHttpPedidos(factory, Guid.NewGuid());
        using var anonimo = ClienteHttpPedidos(factory);
        var token = await CsrfPedidosAsync(admin);
        var geral = new { produtoId = produto.Id, tipo = "Percentual", escopo = "TodasVariantes", valor = "3000" };
        var especifica = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "VariantesSelecionadas",
            valor = "1100", variantesIds = new[] { padrao.Id } };
        var chave = Guid.NewGuid();

        using var semSessao = await anonimo.GetAsync(PromocoesUrl);
        using var proibida = await cliente.GetAsync(PromocoesUrl);
        using var semCsrf = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl, new { configuracao = geral }, chave, null);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);

        var corpo = new { configuracao = geral, ativa = true };
        using var criada = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl, corpo, chave, token);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var registro = await criada.Content.ReadFromJsonAsync<JsonElement>();
        using var replay = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl, corpo, chave, token);
        Assert.Equal(registro.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        using var divergente = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = especifica }, chave, token);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);

        using var previa = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl + "/previa",
            new { configuracao = especifica }, null, token);
        Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
        var linhas = (await previa.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variantes");
        var linha = Assert.Single(linhas.EnumerateArray(), v => v.GetProperty("varianteId").GetGuid() == padrao.Id);
        Assert.True(linha.GetProperty("especificaPrevalece").GetBoolean());
        Assert.Equal("1100", linha.GetProperty("precoEfetivo").GetProperty("finalCentavos").GetString());

        using var criadaEspecifica = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = especifica, ativa = true }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Created, criadaEspecifica.StatusCode);
        using var conflitoEspecifico = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = especifica, ativa = true }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Conflict, conflitoEspecifico.StatusCode);
        var especificaId = (await criadaEspecifica.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var previaConflitante = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl + "/previa",
            new { configuracao = especifica }, null, token);
        var conflitoPrevia = await previaConflitante.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(especificaId, Assert.Single(conflitoPrevia.GetProperty("conflitos").EnumerateArray()).GetGuid());
        Assert.All(conflitoPrevia.GetProperty("variantes").EnumerateArray(),
            v => Assert.False(v.GetProperty("propostaEfetiva").GetBoolean()));
        var rota = $"{PromocoesUrl}/{especificaId:D}";
        using var desativada = await EnviarPromocaoAsync(admin, HttpMethod.Patch, rota + "/status",
            new { ativa = false, versao = 1 }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.OK, desativada.StatusCode);
        var alteracao = especifica with { variantesIds = new[] { alternativa.Id }, valor = "900" };
        using var antiga = await EnviarPromocaoAsync(admin, HttpMethod.Put, rota,
            new { configuracao = alteracao, versao = 1 }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Conflict, antiga.StatusCode);
        using var editada = await EnviarPromocaoAsync(admin, HttpMethod.Put, rota,
            new { configuracao = alteracao, versao = 2 }, Guid.NewGuid(), token);
        Assert.True(editada.IsSuccessStatusCode, await editada.Content.ReadAsStringAsync());
        var detalhe = await admin.GetFromJsonAsync<JsonElement>(rota);
        Assert.Equal(alternativa.Id, Assert.Single(detalhe.GetProperty("variantesIds").EnumerateArray()).GetGuid());
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>(PromocoesUrl + $"?produtoId={produto.Id:D}")).GetArrayLength());

        var publico = await anonimo.GetFromJsonAsync<JsonElement>($"/api/catalogo/produtos/{produto.Id:D}");
        var opcao = Assert.Single(publico.GetProperty("opcoes").EnumerateArray(),
            v => v.GetProperty("id").GetGuid() == padrao.Id);
        Assert.Equal(11.99m, opcao.GetProperty("precoVenda").GetDecimal());
        var compra = await Compra(db).ExecuteAsync(new(clienteId, Guid.NewGuid(), [new(produto.Id, padrao.Id, 1)]));
        Assert.Equal(1199, Assert.IsType<Pedido>(compra.Pedido).TotalCentavos);
    }

    [SqlServerIntegrationFact]
    public async Task Promocoes_AtivacaoECriacaoConcorrentesJanelasAdjacentesEPrecoRegularAlterado()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (produto, padrao, _) = await CriarProdutoCompravelAsync(db);
        using var factory = CriarApiPedidos(banco);
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        var token = await CsrfPedidosAsync(admin);
        var inicio = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var fim = inicio.AddDays(1);
        var config = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "TodasVariantes",
            valor = "900", inicioUtc = inicio, fimUtc = fim };
        using var invalida = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = config with { valor = "1100" }, ativa = true }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);

        using var preparada = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = config }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Created, preparada.StatusCode);
        var id = (await preparada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var concorrentes = await Task.WhenAll(
            EnviarPromocaoAsync(admin, HttpMethod.Patch, $"{PromocoesUrl}/{id:D}/status", new { ativa = true, versao = 1 },
                Guid.NewGuid(), token),
            EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl, new { configuracao = config, ativa = true },
                Guid.NewGuid(), token));
        using var primeira = concorrentes[0];
        using var segunda = concorrentes[1];
        Assert.Single(concorrentes, r => r.IsSuccessStatusCode);
        var conflito = Assert.Single(concorrentes, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("vigencia_sobreposta", (await conflito.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
        using var adjacente = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = config with { inicioUtc = fim, fimUtc = fim.AddDays(1) }, ativa = true }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Created, adjacente.StatusCode);
        Assert.Equal(2, await db.Set<Promocao>().CountAsync(p => p.Ativa));

        var variante = await db.ProdutoVariantes.SingleAsync(v => v.Id == padrao.Id);
        variante.AtualizarDados(variante.Identificacao, variante.UnidadeVenda, 8m);
        await db.SaveChangesAsync();
        using var previa = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl + "/previa",
            new { configuracao = config, instanteUtc = inicio }, null, token);
        Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
        var linha = Assert.Single((await previa.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variantes").EnumerateArray(),
            v => v.GetProperty("varianteId").GetGuid() == padrao.Id);
        Assert.True(linha.GetProperty("precoIncompativel").GetBoolean());
        Assert.Equal("800", linha.GetProperty("precoEfetivo").GetProperty("finalCentavos").GetString());
    }

    private static async Task<HttpResponseMessage> EnviarPromocaoAsync(HttpClient http, HttpMethod metodo,
        string rota, object corpo, Guid? chave, string? token)
    {
        using var req = new HttpRequestMessage(metodo, rota) { Content = JsonContent.Create(corpo) };
        if (chave is { } uuid) req.Headers.Add("Idempotency-Key", uuid.ToString("D"));
        if (token is not null) req.Headers.Add("X-XSRF-TOKEN", token);
        return await http.SendAsync(req);
    }
}
