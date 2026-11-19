using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class AdminEstoqueInsumoSqlServerIntegrationTests
{
    private const string RotaProducao = "/api/admin/producao";
    private static readonly DateTimeOffset AgoraProducao = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    [SqlServerIntegrationFact]
    public async Task OrdensProducao_PrevistaReservaIntegralEIdempotenciaComLoteVencido()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var clock = new AdjustableTimeProvider(AgoraProducao);
        using var factory = CreateFactory(database, clock);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        using var anonimo = CreateClient(factory);
        using var cliente = CreateClient(factory, XaquiamuniRoles.Cliente);
        using var semSessao = await anonimo.GetAsync(RotaProducao);
        using var proibido = await cliente.GetAsync(RotaProducao);
        using var semCsrf = await admin.PostAsJsonAsync(RotaProducao, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        var csrf = await GetCsrfAsync(admin);

        await using var db = database.CreateVerificationContext();
        var erva = InsumoEstoque.Criar("Alecrim", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama, controlaLote: true);
        var baseG = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var receita = await CriarReceitaTesteAsync(db, 3, (erva, 7), (baseG, 6));
        var data = DateOnly.FromDateTime(AgoraProducao.UtcDateTime);
        var primeiro = await EntrarProducaoAsync(admin, csrf, erva.Id, 4, "Com validade", data, data);
        var segundo = await EntrarProducaoAsync(admin, csrf, erva.Id, 3, "Sem validade", data, null);

        var previa = RotaProducao + $"/previa?receitaId={receita.Id:D}&quantidade=3";
        var faltando = (await admin.GetFromJsonAsync<PreviaProducao>(previa))!;
        Assert.Equal(6m, Assert.Single(faltando.Materiais, m => m.InsumoId == baseG.Id).Falta);
        using var inexata = await admin.GetAsync(RotaProducao + $"/previa?receitaId={receita.Id:D}&quantidade=1");
        Assert.Equal(HttpStatusCode.BadRequest, inexata.StatusCode);
        var corpo = new InicioProducaoInput(receita.Id, faltando.ReceitaVersaoId, 3);
        using var falta = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, falta.StatusCode);
        Assert.False(await db.OrdensProducao.AnyAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync());

        await EntrarProducaoAsync(admin, csrf, baseG.Id, 6);
        using var versao = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo with { ReceitaVersaoId = Guid.NewGuid() }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, versao.StatusCode);
        var chave = Guid.NewGuid();
        var concorrentes = await Task.WhenAll(
            EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, chave),
            EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, Guid.NewGuid()));
        using var criada = Assert.Single(concorrentes, r => r.StatusCode == HttpStatusCode.Created);
        using var perdeu = Assert.Single(concorrentes, r => r.StatusCode == HttpStatusCode.Conflict);
        // Identifica qual chave venceu sem presumir escalonamento.
        var chaveVencedora = ReferenceEquals(criada, concorrentes[0]) ? chave :
            (await db.OrdensProducao.AsNoTracking().SingleAsync()).ChaveInicio!.Value;
        var ordemId = (await criada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ordemId").GetGuid();
        using var repetida = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, chaveVencedora);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(ordemId, (await repetida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ordemId").GetGuid());
        using var divergente = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo with { Quantidade = 6 }, chaveVencedora);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.Equal(ordemId, (await admin.GetFromJsonAsync<JsonElement>($"{RotaProducao}/tentativas/{chaveVencedora:D}")).GetProperty("ordemId").GetGuid());

        var depois = (await admin.GetFromJsonAsync<PreviaProducao>(previa))!;
        Assert.All(depois.Materiais, m => { Assert.Equal(m.Necessario, m.Utilizavel); Assert.Equal(0m, m.Livre); Assert.Equal(m.Necessario, m.Reservado); });
        var porLote = await db.AlocacoesMateriasPrimas.Where(a => a.Saldo.LoteId != null)
            .Select(a => new { Lote = a.Saldo.LoteId!.Value, a.Reservada }).ToDictionaryAsync(a => a.Lote, a => a.Reservada);
        Assert.Equal(4m, porLote[primeiro.LoteId!.Value]);
        Assert.Equal(3m, porLote[segundo.LoteId!.Value]);

        clock.SetUtcNow(AgoraProducao.AddDays(1));
        var url = $"{RotaProducao}/{ordemId:D}";
        var detalhe = (await admin.GetFromJsonAsync<DetalheOrdemProducao>(url))!;
        Assert.True(Assert.Single(detalhe.Materiais, m => m.InsumoId == erva.Id).ReservaInelegivel);
        Assert.Equal(ordemId, Assert.Single((await admin.GetFromJsonAsync<ResumoOrdemProducao[]>(
            $"{RotaProducao}?situacao=EmAndamento&produtoId={detalhe.Resumo.ProdutoId:D}&receitaId={receita.Id:D}"))!).Id);

        var alocacoes = db.AlocacoesMateriasPrimas.Where(a => a.Necessidade.OrdemId == ordemId);
        await alocacoes.ExecuteUpdateAsync(s => s.SetProperty(a => a.Consumida, 1m));
        using var comConsumo = await EnviarRecebimentoAsync(admin, csrf, url + "/cancelar", new { }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, comConsumo.StatusCode);
        await alocacoes.ExecuteUpdateAsync(s => s.SetProperty(a => a.Consumida, 0m));

        var chaveCancelar = Guid.NewGuid();
        var cancelamentos = await Task.WhenAll(
            EnviarRecebimentoAsync(admin, csrf, url + "/cancelar", new { }, chaveCancelar),
            EnviarRecebimentoAsync(admin, csrf, url + "/cancelar", new { }, chaveCancelar));
        foreach (var resposta in cancelamentos)
        {
            using (resposta) Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
        var cancelada = (await admin.GetFromJsonAsync<DetalheOrdemProducao>(url))!;
        Assert.Equal("Cancelada", cancelada.Resumo.Situacao);
        Assert.All(cancelada.Materiais, m => { Assert.Equal(0m, m.Reservado); Assert.False(m.ReservaInelegivel); });
        Assert.Equal(3, await db.AlocacoesMateriasPrimas.CountAsync());
        Assert.Equal(3, await db.MovimentosInsumo.CountAsync());
        Assert.Empty(await db.MovimentosProdutoAcabado.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task OrdensProducao_ConcorrenciaComSaidaERecusaCadastrosInativos()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database, new AdjustableTimeProvider(AgoraProducao));
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        await using var db = database.CreateVerificationContext();
        var material = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var receita = await CriarReceitaTesteAsync(db, 1, (material, 7));
        var corpo = new InicioProducaoInput(receita.Id, receita.Atual.Id, 1);
        await EntrarProducaoAsync(admin, csrf, material.Id, 10);

        material.DefinirAtivo(false);
        await db.SaveChangesAsync();
        using var inativo = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, inativo.StatusCode);
        material.DefinirAtivo(true);
        receita.DefinirAtiva(false, AdminId, AgoraProducao);
        await db.SaveChangesAsync();
        using var receitaInativa = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, receitaInativa.StatusCode);
        receita.DefinirAtiva(true, AdminId, AgoraProducao);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ProdutoVariantes] SET [Ativa] = 0 WHERE [Id] = {receita.ProdutoVarianteId}");
        using var varianteInativa = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, varianteInativa.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ProdutoVariantes] SET [Ativa] = 1 WHERE [Id] = {receita.ProdutoVarianteId}");

        using var saidaRequest = CreateEntryRequest($"{AdminEstoqueInsumoEndpoints.BasePath}/{material.Id:D}/saidas",
            new { tipo = "SaidaManual", quantidadeOriginal = 7, unidadeOriginal = "Grama", motivo = "Teste" }, csrf, "producao-saida-paralela");
        var concorrentes = await Task.WhenAll(
            EnviarRecebimentoAsync(admin, csrf, RotaProducao, corpo, Guid.NewGuid()), admin.SendAsync(saidaRequest));
        using var ordem = concorrentes[0];
        using var saida = concorrentes[1];
        Assert.Contains(ordem.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        Assert.Contains(saida.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        Assert.NotEqual(ordem.StatusCode, saida.StatusCode);
        var consulta = new EfEstoqueInsumoConsultaRepository(db);
        Assert.Equal(3m, (await consulta.ConsultarInsumoAsync(material.Id, new(2026, 11, 19)))!.Resumo.SaldoDisponivel);
    }

    private static async Task<ReceitaProducao> CriarReceitaTesteAsync(XaquiamuniDbContext db, int rendimento,
        params (InsumoEstoque Material, decimal Quantidade)[] materiais)
    {
        var categoria = Categoria.Criar("Sabonetes", null, 0);
        var padrao = ProdutoVariante.Criar("Padrão", "UN", 10, true, true);
        var variante = ProdutoVariante.Criar("Lavanda", "UN", 10, true, false);
        var produto = Produto.Criar("Sabonete", null, categoria, [padrao, variante]);
        var receita = ReceitaProducao.Criar(variante, "Receita", rendimento,
            materiais.Select(m => ItemReceitaProducao.Criar(m.Material, m.Quantidade, UnidadeFisicaEstoque.Grama)).ToArray(),
            AdminId, AgoraProducao);
        db.Categorias.Add(categoria);
        db.Produtos.Add(produto);
        db.InsumosEstoque.AddRange(materiais.Select(m => m.Material));
        db.ReceitasProducao.Add(receita);
        await db.SaveChangesAsync();
        return receita;
    }

    private static async Task<MovimentoAdminResponse> EntrarProducaoAsync(HttpClient admin, CsrfState csrf, Guid material,
        decimal quantidade, string? lote = null, DateOnly? entrada = null, DateOnly? validade = null)
    {
        using var request = CreateEntryRequest($"{AdminEstoqueInsumoEndpoints.BasePath}/{material:D}/entradas",
            new EntradaInsumoAdminRequest { Tipo = TipoMovimentoInsumo.EstoqueInicial, QuantidadeOriginal = quantidade,
                UnidadeOriginal = UnidadeFisicaEstoque.Grama, IdentificacaoNovoLote = lote,
                DataEntradaNovoLote = entrada, DataValidadeNovoLote = validade }, csrf, Guid.NewGuid().ToString("D"));
        using var response = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovimentoAdminResponse>())!;
    }
}
