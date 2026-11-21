using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Api.Catalogo;

public static class AdminPromocaoEndpoints
{
    public const string BasePath = "/api/admin/promocoes";

    public static IEndpointRouteBuilder MapAdminPromocaoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthorizationPolicies.Administracao);
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapGet("", async (Guid? produtoId, int? pagina, EfPromocoes repo, CancellationToken ct) =>
            pagina is < 1 or > 1_000_000
                ? Erro(400, "pagina_invalida", "Informe uma página válida.")
                : Results.Ok(await repo.ListarAsync(produtoId, pagina ?? 1, ct)));
        group.MapGet("/{id:guid}", async (Guid id, EfPromocoes repo, CancellationToken ct) =>
            await repo.ConsultarAsync(id, ct) is { } detalhe
                ? Results.Ok(detalhe) : Responder(new(404, "promocao_nao_encontrada")));
        group.MapPost("/previa", PreverAsync).RequireXaquiamuniAntiforgery();
        group.MapPost("", CriarAsync).RequireXaquiamuniAntiforgery();
        group.MapPut("/{id:guid}", EditarAsync).RequireXaquiamuniAntiforgery();
        group.MapPatch("/{id:guid}/status", AlterarStatusAsync).RequireXaquiamuniAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> PreverAsync(HttpContext ctx, EfPromocoes repo, CancellationToken ct)
    {
        var (request, erro) = await AdminPedidosEndpoints.LerComandoAsync<PreviaRequest>(ctx.Request, ct);
        return erro ?? (request?.Configuracao is { } dados
            ? Responder(await repo.PreverAsync(dados, request.SubstituiId, request.InstanteUtc, ct))
            : Responder(new(400, "configuracao_invalida")));
    }

    private static async Task<IResult> CriarAsync(HttpContext ctx, EfPromocoes repo, CancellationToken ct)
    {
        var (request, erro) = await AdminPedidosEndpoints.LerComandoAsync<CriarRequest>(ctx.Request, ct);
        return erro ?? (request?.Configuracao is { } dados
            ? await SalvarAsync(ctx, repo, null, dados, null, request.Ativa ?? false, ct)
            : Responder(new(400, "configuracao_invalida")));
    }

    private static async Task<IResult> EditarAsync(Guid id, HttpContext ctx, EfPromocoes repo, CancellationToken ct)
    {
        var (request, erro) = await AdminPedidosEndpoints.LerComandoAsync<EditarRequest>(ctx.Request, ct);
        return erro ?? (request is { Configuracao: { } dados, Versao: > 0 }
            ? await SalvarAsync(ctx, repo, id, dados, request.Versao, null, ct)
            : Responder(new(400, "configuracao_invalida")));
    }

    private static async Task<IResult> AlterarStatusAsync(Guid id, HttpContext ctx, EfPromocoes repo, CancellationToken ct)
    {
        var (request, erro) = await AdminPedidosEndpoints.LerComandoAsync<StatusRequest>(ctx.Request, ct);
        return erro ?? (request is { Ativa: not null, Versao: > 0 }
            ? await SalvarAsync(ctx, repo, id, null, request.Versao, request.Ativa, ct)
            : Responder(new(400, "configuracao_invalida")));
    }

    private static async Task<IResult> SalvarAsync(HttpContext ctx, EfPromocoes repo, Guid? id, ConfigurarPromocao? dados,
        int? versao, bool? ativa, CancellationToken ct)
    {
        if (!Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var autor) || autor == Guid.Empty)
            return Results.Unauthorized();
        if (ctx.Request.Headers["Idempotency-Key"] is not { Count: 1 } chaves ||
            !Guid.TryParseExact(chaves[0], "D", out var chave) || chave == Guid.Empty)
            return Erro(400, "chave_invalida", "Informe uma Idempotency-Key UUID válida.");
        return Responder(await repo.SalvarAsync(id, dados, versao, ativa, autor, chave, ct));
    }

    private static IResult Responder(ResultadoPromocao r) => r switch
    {
        { Status: 201, Detalhe: { } detalhe } => Results.Created($"{BasePath}/{detalhe.Id:D}", detalhe),
        { Status: 200 } => Results.Ok((object?)r.Detalhe ?? r.Previa),
        _ => Erro(r.Status, r.Codigo!, r.Codigo switch
        {
            "versao_divergente" => "A promoção mudou. Consulte a versão atual.",
            "vigencia_sobreposta" => "Já existe promoção ativa no mesmo escopo e período.",
            "preco_promocional_invalido" => "O preço promocional deve ser positivo e inferior ao regular em todas as variantes ativas abrangidas.",
            "variantes_invalidas" => "Selecione variantes que pertençam ao produto.",
            "chave_em_uso" => "Esta chave foi utilizada em outra operação.",
            "promocao_nao_encontrada" => "Promoção não encontrada.",
            "produto_nao_encontrado" => "Produto não encontrado.",
            "conflito_concorrencia" => "Outra operação está em andamento. Tente novamente.",
            _ => "Revise o tipo, o escopo, o valor, as variantes e a vigência."
        })
    };

    private static IResult Erro(int status, string codigo, string detalhe) =>
        Results.Problem(statusCode: status, detail: detalhe, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

    private sealed record CriarRequest(ConfigurarPromocao? Configuracao, bool? Ativa);
    private sealed record EditarRequest(ConfigurarPromocao? Configuracao, int? Versao);
    private sealed record StatusRequest(bool? Ativa, int? Versao);
    private sealed record PreviaRequest(ConfigurarPromocao? Configuracao, Guid? SubstituiId, DateTimeOffset? InstanteUtc);
}
