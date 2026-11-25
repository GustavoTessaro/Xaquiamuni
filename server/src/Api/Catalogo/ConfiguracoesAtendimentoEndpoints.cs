using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Api.Catalogo;

public static class ConfiguracoesAtendimentoEndpoints
{
    public static void MapConfiguracoesAtendimentoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publico = endpoints.MapGroup("/api/loja").AllowAnonymous().AddEndpointFilter(SemCacheAsync);
        publico.MapGet("/modalidades-entrega", async (IConfiguracoesAtendimento api, CancellationToken ct) =>
            Results.Ok(await api.ModalidadesAsync(ct)));
        publico.MapGet("/contato", async (IConfiguracoesAtendimento api, CancellationToken ct) =>
            Results.Ok(new { whatsApp = await api.ContatoAsync(ct) }));

        var admin = endpoints.MapGroup("/api/admin/loja/atendimento")
            .RequireAuthorization(AuthorizationPolicies.Administracao).AddEndpointFilter(SemCacheAsync);
        admin.MapGet("", async (IConfiguracoesAtendimento api, CancellationToken ct) =>
            Results.Ok(await api.ConsultarAsync(ct)));
        admin.MapGet("/cidades", async (IConfiguracoesAtendimento api, CancellationToken ct) =>
            Results.Ok(await api.CidadesAsync(ct)));
        admin.MapGet("/municipios", () =>
            Results.Ok(CatalogoMunicipiosIbge.Estados.Select(estado => new { estado.Uf, estado.Nome })));
        admin.MapGet("/municipios/{uf}", IResult (string uf) =>
            CatalogoMunicipiosIbge.Estados.FirstOrDefault(estado =>
                string.Equals(estado.Uf, uf, StringComparison.OrdinalIgnoreCase)) is { } estado
                ? Results.Ok(estado.Municipios) : Results.NotFound());
        admin.MapGet("/cidades/{id:guid}", async Task<IResult> (Guid id, IConfiguracoesAtendimento api, CancellationToken ct) =>
            await api.CidadeAsync(id, ct) is { } cidade ? Results.Ok(cidade) : Results.NotFound());

        admin.MapPut("/modalidades", (HttpContext ctx, IConfiguracoesAtendimento api, CancellationToken ct) =>
            ComandoAsync<SalvarModalidades>(ctx, (input, autor, chave) => api.AtualizarModalidadesAsync(input, autor, chave, ct), ct))
            .RequireXaquiamuniAntiforgery();
        admin.MapPut("/whatsapp", (HttpContext ctx, IConfiguracoesAtendimento api, CancellationToken ct) =>
            ComandoAsync<SalvarWhatsApp>(ctx, (input, autor, chave) => api.AtualizarWhatsAppAsync(input, autor, chave, ct), ct))
            .RequireXaquiamuniAntiforgery();
        admin.MapPut("/correios-manual", (HttpContext ctx, IConfiguracoesAtendimento api, CancellationToken ct) =>
            ComandoAsync<SalvarCorreiosManual>(ctx, (input, autor, chave) => api.AtualizarCorreiosManualAsync(input, autor, chave, ct), ct))
            .RequireXaquiamuniAntiforgery();
        admin.MapPost("/cidades", (HttpContext ctx, IConfiguracoesAtendimento api, CancellationToken ct) =>
            ComandoAsync<SalvarCidade>(ctx, (input, autor, chave) => api.SalvarCidadeAsync(null, input, autor, chave, ct), ct, 201))
            .RequireXaquiamuniAntiforgery();
        admin.MapPut("/cidades/{id:guid}", (Guid id, HttpContext ctx, IConfiguracoesAtendimento api, CancellationToken ct) =>
            ComandoAsync<SalvarCidade>(ctx, (input, autor, chave) => api.SalvarCidadeAsync(id, input, autor, chave, ct), ct))
            .RequireXaquiamuniAntiforgery();
        admin.MapDelete("/cidades/{id:guid}", (Guid id, int? revisao, HttpContext ctx, IConfiguracoesAtendimento api, CancellationToken ct) =>
            ExecutarAsync(ctx, (autor, chave) => api.ExcluirCidadeAsync(id, revisao, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();
    }

    private static ValueTask<object?> SemCacheAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        ctx.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(ctx);
    }

    private static Task<IResult> ComandoAsync<T>(HttpContext ctx, Func<T, Guid, Guid, Task<ResultadoAtendimento>> aplicar,
        CancellationToken ct, int sucesso = 200) where T : class => ExecutarAsync(ctx, async (autor, chave) =>
    {
        var (input, erro) = await AdminPedidosEndpoints.LerComandoAsync<T>(ctx.Request, ct);
        if (erro is not null) return erro;
        return Responder(await aplicar(input!, autor, chave), sucesso);
    });

    private static Task<IResult> ExecutarAsync(HttpContext ctx, Func<Guid, Guid, Task<ResultadoAtendimento>> aplicar) =>
        ExecutarAsync(ctx, async (autor, chave) => Responder(await aplicar(autor, chave)));

    private static async Task<IResult> ExecutarAsync(HttpContext ctx, Func<Guid, Guid, Task<IResult>> aplicar)
    {
        if (!Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var autor) || autor == Guid.Empty)
            return Results.Forbid();
        var chaves = ctx.Request.Headers["Idempotency-Key"];
        if (chaves.Count != 1 || !Guid.TryParseExact(chaves[0], "D", out var chave) || chave == Guid.Empty)
            return Results.BadRequest(new { mensagem = "Informe Idempotency-Key em formato UUID." });
        return await aplicar(autor, chave);
    }

    private static IResult Responder(ResultadoAtendimento resultado, int sucesso = 200) => resultado.Status switch
    {
        StatusAtendimento.Sucesso => Results.Json(resultado.Dados, statusCode: sucesso),
        StatusAtendimento.Ausente => Results.NotFound(),
        StatusAtendimento.Conflito => Results.Problem(statusCode: 409, detail: resultado.Erro,
            extensions: resultado.Codigo is null ? null : new Dictionary<string, object?> { ["codigo"] = resultado.Codigo }),
        _ => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["atendimento"] = [resultado.Erro ?? "Confira os dados informados."]
        })
    };
}
