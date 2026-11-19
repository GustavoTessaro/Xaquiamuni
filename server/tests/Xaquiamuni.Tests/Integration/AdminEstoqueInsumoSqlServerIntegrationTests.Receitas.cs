using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class AdminEstoqueInsumoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ReceitasProducao_AdminCsrfVersoesConcorrenciaEEstoqueIntacto()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var anonimo = CreateClient(factory);
        using var cliente = CreateClient(factory, XaquiamuniRoles.Cliente);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        const string rota = "/api/admin/receitas";
        using var semSessao = await anonimo.GetAsync(rota);
        using var proibida = await cliente.GetAsync(rota);
        using var semCsrf = await admin.PostAsJsonAsync(rota, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        var csrf = await GetCsrfAsync(admin);

        var categoria = Categoria.Criar("Sabonetes", null, 0);
        var variante = ProdutoVariante.Criar("Lavanda", "UN", 10, true, true);
        var inativa = ProdutoVariante.Criar("Inativa", "UN", 10, false, false);
        var produto = Produto.Criar("Sabonete", null, categoria, [variante, inativa]);
        var baseG = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        await using (var db = database.CreateVerificationContext())
        {
            db.Categorias.Add(categoria);
            db.Produtos.Add(produto);
            db.InsumosEstoque.Add(baseG);
            await db.SaveChangesAsync();
        }

        async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string url, object corpo)
        {
            using var requisicao = new HttpRequestMessage(metodo, url) { Content = JsonContent.Create(corpo) };
            AddAntiforgery(requisicao, csrf);
            return await admin.SendAsync(requisicao);
        }
        var dados = new ReceitaInput(variante.Id, "Receita lavanda", 2, [new(baseG.Id, "100.000001", "Grama")], null);
        using var varianteInativa = await Enviar(HttpMethod.Post, rota, dados with { ProdutoVarianteId = inativa.Id });
        Assert.Equal(HttpStatusCode.BadRequest, varianteInativa.StatusCode);
        using var unidadeInvalida = await Enviar(HttpMethod.Post, rota, dados with { Itens = [new(baseG.Id, "100", "Mililitro")] });
        Assert.Equal(HttpStatusCode.BadRequest, unidadeInvalida.StatusCode);

        using var criado = await Enviar(HttpMethod.Post, rota, dados);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var url = Assert.IsType<Uri>(criado.Headers.Location).ToString();
        var inicial = (await criado.Content.ReadFromJsonAsync<AdminReceitasProducaoEndpoints.DetalheReceita>())!;
        Assert.Equal(1, inicial.Atual.Numero);
        using var duplicada = await Enviar(HttpMethod.Post, rota, dados);
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);

        var editando = dados with { Token = inicial.Token, Rendimento = 4 };
        var respostas = await Task.WhenAll(Enviar(HttpMethod.Put, url, editando), Enviar(HttpMethod.Put, url, editando));
        using var sucesso = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        using var conflito = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var alterada = (await sucesso.Content.ReadFromJsonAsync<AdminReceitasProducaoEndpoints.DetalheReceita>())!;
        Assert.Equal(2, alterada.Atual.Numero);
        Assert.Equal(4, alterada.Atual.Rendimento);
        Assert.Equal([2, 1], alterada.Historico.Select(v => v.Numero));
        var anterior = (await admin.GetFromJsonAsync<AdminReceitasProducaoEndpoints.VersaoReceita>(url + "/versoes/1"))!;
        Assert.Equal(2, anterior.Rendimento);
        Assert.Equal("100.000001", Assert.Single(anterior.Itens).Quantidade);
        using var inativada = await Enviar(HttpMethod.Patch, url + "/status", new { ativa = false, token = alterada.Token });
        Assert.Equal(HttpStatusCode.OK, inativada.StatusCode);
        var snapshot = (await inativada.Content.ReadFromJsonAsync<AdminReceitasProducaoEndpoints.DetalheReceita>())!;
        Assert.False(snapshot.Resumo.Ativa);

        await using var verificacao = database.CreateVerificationContext();
        var material = await verificacao.InsumosEstoque.SingleAsync();
        material.DefinirAtivo(false);
        await verificacao.SaveChangesAsync();
        using var reativacao = await Enviar(HttpMethod.Patch, url + "/status", new { ativa = true, token = snapshot.Token });
        Assert.Equal(HttpStatusCode.BadRequest, reativacao.StatusCode);
        using var materialInativo = await Enviar(HttpMethod.Post, rota, dados);
        Assert.Equal(HttpStatusCode.BadRequest, materialInativo.StatusCode);
        using var consultaHistorica = await admin.GetAsync(url + "/versoes/1");
        Assert.Equal(HttpStatusCode.OK, consultaHistorica.StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<AdminReceitasProducaoEndpoints.ResumoReceita[]>(rota))!);

        Assert.False(await verificacao.SaldosInsumo.AnyAsync());
        Assert.False(await verificacao.MovimentosInsumo.AnyAsync());
        Assert.False(await verificacao.LotesInsumo.AnyAsync());
        Assert.False(await verificacao.SaldosProdutoAcabado.AnyAsync());
        Assert.False(await verificacao.MovimentosProdutoAcabado.AnyAsync());
    }
}
