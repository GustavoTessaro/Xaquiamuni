using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Agenda;
using Xaquiamuni.Dominio.Agenda;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string AgendaUrl = "/api/admin/agenda";
    private static readonly DateOnly DiaAgenda = new(2026, 11, 30);

    [SqlServerIntegrationFact]
    public async Task Agenda_ConsultaCivilOrdenadaComAutorizacaoEIdempotencia()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        var diaInteiro = new CompromissoAgendaInput("Feira", null, null, DiaAgenda, null, null, null);
        var chave = Guid.NewGuid();

        using var proibido = await cliente.Http.GetAsync(AgendaUrl);
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        using var semCsrf = await admin.Http.PostAsJsonAsync(AgendaUrl, diaInteiro);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using var semChave = await admin.EnviarAsync(AgendaUrl, diaInteiro);
        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        using var invalido = await admin.EnviarAsync(AgendaUrl, diaInteiro with { HorarioFinal = new(9, 0) }, chave);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);

        var respostas = await Task.WhenAll(admin.EnviarAsync(AgendaUrl, diaInteiro, chave),
            admin.EnviarAsync(AgendaUrl, diaInteiro, chave));
        using var primeira = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        using var repetida = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        var criado = (await primeira.Content.ReadFromJsonAsync<CompromissoAgendaDto>())!;
        Assert.Equal(criado, await repetida.Content.ReadFromJsonAsync<CompromissoAgendaDto>());
        Assert.Equal(DiaAgenda, criado.Data);
        Assert.Null(criado.HorarioInicial);
        using var divergente = await admin.EnviarAsync(AgendaUrl, diaInteiro with { Titulo = "Outro" }, chave);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);

        var tentativa = AgendaUrl + $"/tentativas/{chave:D}";
        Assert.Equal(new TentativaAgendaDto("criar", criado.Id, criado), await admin.Http.GetFromJsonAsync<TentativaAgendaDto>(tentativa));
        using var alheia = await outro.Http.GetAsync(tentativa);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        Assert.Equal(criado, await outro.Http.GetFromJsonAsync<CompromissoAgendaDto>(AgendaUrl + $"/{criado.Id:D}"));

        var tarde = await admin.PostAsync<CompromissoAgendaDto>(AgendaUrl, diaInteiro with { HorarioInicial = new(23, 45) },
            HttpStatusCode.Created, Guid.NewGuid());
        var cedo = await admin.PostAsync<CompromissoAgendaDto>(AgendaUrl,
            diaInteiro with { HorarioInicial = new(8, 0), HorarioFinal = new(9, 0), Descricao = "Equipe", Categoria = "Loja" },
            HttpStatusCode.Created, Guid.NewGuid());
        await admin.PostAsync<CompromissoAgendaDto>(AgendaUrl, diaInteiro with { Data = DiaAgenda.AddDays(1) },
            HttpStatusCode.Created, Guid.NewGuid());

        using var lista = await admin.Http.GetAsync(AgendaUrl + "?dataInicial=2026-11-30&dataFinal=2026-11-30");
        Assert.True(lista.Headers.CacheControl?.NoStore);
        var compromissos = (await lista.Content.ReadFromJsonAsync<CompromissoAgendaDto[]>())!;
        Assert.Equal(new[] { criado, cedo, tarde }, compromissos);
        using var intervaloInvalido = await admin.Http.GetAsync(AgendaUrl + "?dataInicial=2026-12-01&dataFinal=2026-11-30");
        Assert.Equal(HttpStatusCode.BadRequest, intervaloInvalido.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task Agenda_SerializaEdicoesERecuperaExclusaoLogica()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var input = new CompromissoAgendaInput("Compromisso", null, null, DiaAgenda, null, null, null);
        var criado = await admin.PostAsync<CompromissoAgendaDto>(AgendaUrl, input, HttpStatusCode.Created, Guid.NewGuid());
        var url = AgendaUrl + $"/{criado.Id:D}";
        var edicao = input with { Titulo = "Editado", Versao = criado.Versao };
        var chaveEdicao = Guid.NewGuid();
        using var semCsrf = await admin.Http.PutAsJsonAsync(url, edicao);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);

        var respostas = await Task.WhenAll(EnviarAgendaAsync(admin, HttpMethod.Put, url, edicao, chaveEdicao),
            EnviarAgendaAsync(outro, HttpMethod.Put, url, edicao, chaveEdicao));
        using var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        using var perdedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var alterado = (await vencedora.Content.ReadFromJsonAsync<CompromissoAgendaDto>())!;
        Assert.NotEqual(criado.Versao, alterado.Versao);
        Assert.Equal(criado.CriadoPorId, alterado.CriadoPorId);
        var autor = alterado.AtualizadoPorId == criado.CriadoPorId ? admin : outro;
        using var replayEdicao = await EnviarAgendaAsync(autor, HttpMethod.Put, url, edicao, chaveEdicao);
        Assert.Equal(HttpStatusCode.OK, replayEdicao.StatusCode);
        Assert.Equal(alterado, await replayEdicao.Content.ReadFromJsonAsync<CompromissoAgendaDto>());

        var excluir = url + $"?versao={alterado.Versao:D}";
        using var deleteSemCsrf = await admin.Http.DeleteAsync(excluir);
        Assert.Equal(HttpStatusCode.BadRequest, deleteSemCsrf.StatusCode);
        var chaveExclusao = Guid.NewGuid();
        using var respondida = await EnviarAgendaAsync(admin, HttpMethod.Delete, excluir, null, chaveExclusao);
        Assert.Equal(HttpStatusCode.OK, respondida.StatusCode);
        var excluido = (await respondida.Content.ReadFromJsonAsync<CompromissoAgendaDto>())!;
        Assert.NotNull(excluido.ExcluidoEmUtc);
        Assert.Equal(criado.CriadoPorId, excluido.ExcluidoPorId);
        using var repetida = await EnviarAgendaAsync(admin, HttpMethod.Delete, excluir, null, chaveExclusao);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(excluido, await repetida.Content.ReadFromJsonAsync<CompromissoAgendaDto>());
        using var novaChave = await EnviarAgendaAsync(admin, HttpMethod.Delete, excluir, null, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, novaChave.StatusCode);
        using var ausente = await admin.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Empty((await admin.Http.GetFromJsonAsync<CompromissoAgendaDto[]>(AgendaUrl))!);
        Assert.Equal(new TentativaAgendaDto("excluir", criado.Id, excluido),
            await admin.Http.GetFromJsonAsync<TentativaAgendaDto>(AgendaUrl + $"/tentativas/{chaveExclusao:D}"));
        Assert.NotNull((await db.Set<CompromissoAgenda>().SingleAsync()).ExcluidoEmUtc);
    }

    [SqlServerIntegrationFact]
    public async Task Agenda_ReverteCompromissoSeRegistroDeIdempotenciaFalhar()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OperacoesAgenda] ADD CONSTRAINT [CK_FalhaAgendaTeste] CHECK (1 = 0)");
        var input = new CompromissoAgendaInput("Compromisso", null, null, DiaAgenda, null, null, null);
        var chave = Guid.NewGuid();
        using var falha = await admin.EnviarAsync(AgendaUrl, input, chave);
        Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
        Assert.False(await db.Set<CompromissoAgenda>().AnyAsync());
        using var ausente = await admin.Http.GetAsync(AgendaUrl + $"/tentativas/{chave:D}");
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OperacoesAgenda] DROP CONSTRAINT [CK_FalhaAgendaTeste]");
        await admin.PostAsync<CompromissoAgendaDto>(AgendaUrl, input, HttpStatusCode.Created, chave);
        Assert.Equal(1, await db.Set<CompromissoAgenda>().CountAsync());
    }

    private static async Task<HttpResponseMessage> EnviarAgendaAsync(Sessao sessao, HttpMethod metodo,
        string url, CompromissoAgendaInput? input, Guid chave)
    {
        var csrf = await sessao.Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(metodo, url);
        if (input is not null) request.Content = JsonContent.Create(input);
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        request.Headers.Add("Idempotency-Key", chave.ToString("D"));
        return await sessao.Http.SendAsync(request);
    }
}
