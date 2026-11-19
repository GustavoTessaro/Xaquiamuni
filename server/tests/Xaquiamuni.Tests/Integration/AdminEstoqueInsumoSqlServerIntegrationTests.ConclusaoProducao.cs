using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class AdminEstoqueInsumoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ConclusaoProducao_AtomicaFefoConsumoRealSobrasERepeticao()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var clock = new AdjustableTimeProvider(AgoraProducao);
        using var factory = CreateFactory(database, clock);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        using var anonimo = CreateClient(factory);
        using var cliente = CreateClient(factory, XaquiamuniRoles.Cliente);
        var csrf = await GetCsrfAsync(admin);
        await using var db = database.CreateVerificationContext();
        var baseG = InsumoEstoque.Criar("Base", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var erva = InsumoEstoque.Criar("Erva", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama, controlaLote: true);
        var receita = await CriarReceitaTesteAsync(db, 1, (baseG, 5), (erva, 6));
        var hoje = DateOnly.FromDateTime(AgoraProducao.UtcDateTime);
        await EntrarProducaoAsync(admin, csrf, baseG.Id, 5);
        var primeiro = await EntrarProducaoAsync(admin, csrf, erva.Id, 3, "Primeiro", hoje, hoje);
        var segundo = await EntrarProducaoAsync(admin, csrf, erva.Id, 3, "Segundo", hoje, null);
        var inicio = new InicioProducaoInput(receita.Id, receita.Atual.Id, 1);
        using var criada = await EnviarRecebimentoAsync(admin, csrf, RotaProducao, inicio, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var ordemId = (await criada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ordemId").GetGuid();
        var url = $"{RotaProducao}/{ordemId:D}/concluir";
        var detalhe = (await admin.GetFromJsonAsync<DetalheOrdemProducao>($"{RotaProducao}/{ordemId:D}"))!;
        var input = new ConcluirProducaoInput(detalhe.Resumo.VersaoOrdem, 3,
            [new(baseG.Id, "Grama", "5"), new(erva.Id, "Grama", "4")], null, hoje.AddDays(5));
        var chave = Guid.NewGuid();
        using var semSessao = await anonimo.PostAsJsonAsync(url, input);
        using var proibida = await cliente.PostAsJsonAsync(url, input);
        using var semCsrf = await admin.PostAsJsonAsync(url, input);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        foreach (var valor in new[] { "0", "-1", "1.0000001", "7" })
        {
            using var invalido = await EnviarRecebimentoAsync(admin, csrf, url,
                input with { Consumos = [input.Consumos![0], new(erva.Id, "Grama", valor)] }, chave);
            Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        }
        using var desatualizada = await EnviarRecebimentoAsync(admin, csrf, url, input with { Versao = Guid.NewGuid() }, chave);
        Assert.Equal(HttpStatusCode.Conflict, desatualizada.StatusCode);
        using var omitida = await EnviarRecebimentoAsync(admin, csrf, url,
            input with { Consumos = [input.Consumos![0]] }, chave);
        Assert.Equal(HttpStatusCode.BadRequest, omitida.StatusCode);
        using var fracionada = await EnviarRecebimentoAsync(admin, csrf, url,
            input with { Consumos = [input.Consumos![0], new(erva.Id, "Unidade", "1.5")] }, chave);
        Assert.Equal(HttpStatusCode.BadRequest, fracionada.StatusCode);
        var loteBloqueado = await db.LotesInsumo.SingleAsync(l => l.Id == primeiro.LoteId);
        loteBloqueado.DefinirBloqueado(true);
        await db.SaveChangesAsync();
        using var bloqueada = await EnviarRecebimentoAsync(admin, csrf, url, input, chave);
        Assert.Equal(HttpStatusCode.Conflict, bloqueada.StatusCode);
        loteBloqueado.DefinirBloqueado(false);
        await db.SaveChangesAsync();
        clock.SetUtcNow(AgoraProducao.AddDays(1));
        using var vencida = await EnviarRecebimentoAsync(admin, csrf, url, input, chave);
        Assert.Equal(HttpStatusCode.Conflict, vencida.StatusCode);
        clock.SetUtcNow(AgoraProducao);

        using var semValidade = await EnviarRecebimentoAsync(admin, csrf, url, input with { DataValidade = null }, chave);
        Assert.Equal(HttpStatusCode.BadRequest, semValidade.StatusCode);
        Assert.Equal(3, await db.MovimentosInsumo.CountAsync());
        Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [FalhaEntradaProducaoTeste] ON [MovimentosProdutoAcabado] AFTER INSERT
            AS THROW 51017, 'Falha de teste na entrada de produção', 1;
            """);
        using var falha = await EnviarRecebimentoAsync(admin, csrf, url, input, chave);
        Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
        Assert.Equal(3, await db.MovimentosInsumo.CountAsync());
        Assert.False(await db.LotesProdutoAcabado.AnyAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync(a => a.Consumida != 0m));
        Assert.Equal(SituacaoOrdemProducao.EmAndamento, (await db.OrdensProducao.AsNoTracking().SingleAsync()).Situacao);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [FalhaEntradaProducaoTeste]");

        var chaves = new[] { chave, Guid.NewGuid() };
        var operacoes = await Task.WhenAll(chaves.Select(k => EnviarRecebimentoAsync(admin, csrf, url, input, k)));
        using var sucesso = Assert.Single(operacoes, r => r.StatusCode == HttpStatusCode.OK);
        using var conflito = Assert.Single(operacoes, r => r.StatusCode == HttpStatusCode.Conflict);
        var vencedora = ReferenceEquals(operacoes[0], sucesso) ? chaves[0] : chaves[1];
        var resposta = (await sucesso.Content.ReadFromJsonAsync<ConclusaoProducao>())!;
        Assert.Equal("Concluida", resposta.Situacao);
        Assert.NotEqual(input.Versao, resposta.Versao);
        Assert.Equal(3, resposta.QuantidadeProduzida);
        Assert.Equal(AdminId, resposta.ResponsavelId);
        Assert.Equal(AgoraProducao, resposta.ConcluidaEmUtc);
        var detalheConcluido = (await admin.GetFromJsonAsync<DetalheOrdemProducao>($"{RotaProducao}/{ordemId:D}"))!;
        Assert.NotEqual(Guid.Empty, detalheConcluido.VarianteId);
        Assert.Equal(resposta.MovimentoEntradaId, detalheConcluido.Conclusao?.MovimentoEntradaId);
        Assert.Equal(resposta.Versao, detalheConcluido.Resumo.VersaoOrdem);
        Assert.Equal(2m, Assert.Single(resposta.Consumos, c => c.InsumoId == erva.Id).Liberada);
        Assert.Equal(0m, Assert.Single(resposta.Consumos, c => c.InsumoId == baseG.Id).Liberada);
        Assert.Equal(3, (await db.SaldosProdutoAcabado.SingleAsync()).QuantidadeVendavel);
        var alocacoes = await db.AlocacoesMateriasPrimas.Select(a => new { a.Saldo.LoteId, a.Consumida, a.MovimentoConsumoId }).ToListAsync();
        Assert.Equal(3m, Assert.Single(alocacoes, a => a.LoteId == primeiro.LoteId).Consumida);
        Assert.Equal(1m, Assert.Single(alocacoes, a => a.LoteId == segundo.LoteId).Consumida);
        Assert.All(alocacoes, a => Assert.NotNull(a.MovimentoConsumoId));
        Assert.Equal(6, await db.MovimentosInsumo.CountAsync());
        Assert.Single(await db.MovimentosProdutoAcabado.ToListAsync());
        using var repetir = await EnviarRecebimentoAsync(admin, csrf, url, input, vencedora);
        Assert.Equal(HttpStatusCode.OK, repetir.StatusCode);
        Assert.Equal(await sucesso.Content.ReadAsStringAsync(), await repetir.Content.ReadAsStringAsync());
        using var divergente = await EnviarRecebimentoAsync(admin, csrf, url, input with { QuantidadeProduzida = 4 }, vencedora);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        var movimentoErva = Assert.Single(alocacoes, a => a.LoteId == primeiro.LoteId).MovimentoConsumoId;
        using var estorno = await EnviarRecebimentoAsync(admin, csrf,
            $"{AdminEstoqueInsumoEndpoints.BasePath}/{erva.Id:D}/movimentacoes/{movimentoErva:D}/estorno",
            new EstornoInsumoAdminRequest { Motivo = "Teste" }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, estorno.StatusCode);
    }
}
