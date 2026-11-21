using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Aplicacao.Catalogo;

namespace Xaquiamuni.Api.Catalogo;

public static class TemaLojaEndpoints
{
    public static IEndpointRouteBuilder MapTemaLojaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/loja/tema", ConsultarPublicoAsync).AllowAnonymous();
        var admin = endpoints.MapGroup("/api/admin/loja/tema").RequireAuthorization(AuthorizationPolicies.Administracao);
        admin.MapGet("", async (HttpContext ctx, IPublicacaoTemaLoja temas, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { publicacao = await temas.ConsultarAsync(ct), temas = TemasLoja.Catalogo });
        });
        admin.MapPut("", PublicarAsync).RequireXaquiamuniAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ConsultarPublicoAsync(HttpContext ctx, IPublicacaoTemaLoja temas, CancellationToken ct)
    {
        var publicacao = await temas.ConsultarAsync(ct);
        var chave = TemasLoja.Buscar(publicacao.Chave)?.Disponivel == true ? publicacao.Chave : TemasLoja.Original;
        var etag = new EntityTagHeaderValue($"\"{chave}-{publicacao.Revisao}\"");
        ctx.Response.Headers.CacheControl = "public, no-cache";
        ctx.Response.GetTypedHeaders().ETag = etag;
        if (ctx.Request.GetTypedHeaders().IfNoneMatch.Any(valor => valor == EntityTagHeaderValue.Any ||
            valor.Compare(etag, useStrongComparison: false)))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        return Results.Ok(new { chave, publicacao.Revisao });
    }

    private static async Task<IResult> PublicarAsync(HttpContext ctx, IPublicacaoTemaLoja temas, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var administradorId) ||
            administradorId == Guid.Empty) return Results.Unauthorized();
        var (comando, erro) = await AdminPedidosEndpoints.LerComandoAsync<PublicarTemaRequest>(ctx.Request, ct);
        if (erro is not null) return erro;
        if (!Guid.TryParseExact(ctx.Request.Headers["Idempotency-Key"], "D", out var idempotencia) ||
            idempotencia == Guid.Empty)
            return Invalido("Idempotency-Key", "Informe uma chave UUID válida.");
        if (comando?.RevisaoEsperada is not (>= 0 and < int.MaxValue))
            return Invalido("revisaoEsperada", "Informe a revisão atual da publicação.");
        var tema = TemasLoja.Buscar(comando.Chave);
        if (tema is null) return Invalido("chave", "Tema desconhecido.");
        if (!tema.Disponivel) return Invalido("chave", "Este tema ainda está em preparação.");

        var publicacao = await temas.PublicarAsync(tema.Chave, comando.RevisaoEsperada.Value, administradorId, idempotencia, ct);
        return publicacao is null
            ? Results.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "A publicação foi alterada ou a chave já foi utilizada. Consulte a revisão atual.")
            : Results.Ok(publicacao);
    }

    private static IResult Invalido(string campo, string mensagem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [mensagem] });

    private sealed record PublicarTemaRequest(string? Chave, int? RevisaoEsperada);
}
