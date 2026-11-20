using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Api.Estoque;

public static class AdminVendasExternasEndpoints
{
    private const string Rota = "/api/admin/vendas-externas";

    public static void MapAdminVendasExternasEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Rota).RequireAuthorization(AuthorizationPolicies.Administracao);
        group.AddEndpointFilter((context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return next(context);
        });
        group.MapGet("", ListarAsync);
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, IGerenciarVendasExternas vendas, CancellationToken ct) =>
            await vendas.ConsultarAsync(id, ct) is { } venda ? Results.Ok(venda) : Results.NotFound());
        group.MapGet("/tentativas/{chave:guid}", async Task<IResult> (Guid chave, string? operacao, HttpContext ctx, IGerenciarVendasExternas vendas, CancellationToken ct) =>
        {
            if (!Autor(ctx, out var autor)) return Results.Forbid();
            if (operacao is not (null or "criar" or "receber" or "cancelar")) return Invalida("Informe uma operação válida.");
            return await vendas.ConsultarTentativaAsync(autor, chave, operacao ?? "criar", ct) is { } venda ? Results.Ok(venda) : Results.NotFound();
        });
        group.MapPost("", (CriarVendaExternaInput input, HttpContext ctx, IGerenciarVendasExternas vendas, CancellationToken ct) =>
            OperarAsync(ctx, (autor, chave) => vendas.CriarAsync(input, autor, chave, ct), criar: true))
            .RequireXaquiamuniAntiforgery();
        group.MapPost("/{id:guid}/recebimentos", (Guid id, ReceberVendaExternaInput input, HttpContext ctx, IGerenciarVendasExternas vendas, CancellationToken ct) =>
            OperarAsync(ctx, (autor, chave) => vendas.ReceberAsync(id, input, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();
        group.MapPost("/{id:guid}/cancelar", (Guid id, CancelarVendaExternaInput input, HttpContext ctx, IGerenciarVendasExternas vendas, CancellationToken ct) =>
            OperarAsync(ctx, (autor, chave) => vendas.CancelarAsync(id, input, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();
    }

    private static async Task<IResult> ListarAsync(string? canal, string? situacao, string? pagamento, int? pagina,
        IGerenciarVendasExternas vendas, CancellationToken ct)
    {
        if (!Filtro(canal, out CanalVendaExterna? filtroCanal) || !Filtro(situacao, out SituacaoVendaExterna? filtroSituacao)
            || pagamento is not (null or "Pendente" or "Parcial" or "Paga") || pagina is < 1 or > 10000)
            return Invalida("Confira canal, situação e página.");
        return Results.Ok(await vendas.ListarAsync(filtroCanal, filtroSituacao, pagamento, pagina ?? 1, ct));
    }

    private static bool Filtro<T>(string? texto, out T? filtro) where T : struct, Enum
    {
        filtro = null;
        if (texto is null) return true;
        if (!Enum.TryParse<T>(texto, out var valor) || !Enum.IsDefined(valor) || valor.ToString() != texto) return false;
        filtro = valor;
        return true;
    }

    private static async Task<IResult> OperarAsync(HttpContext ctx, Func<Guid, Guid, Task<VendaExternaResultado>> executar,
        bool criar = false)
    {
        if (!Autor(ctx, out var autor)) return Results.Forbid();
        if (!Guid.TryParseExact(ctx.Request.Headers["Idempotency-Key"].ToString(), "D", out var chave) || chave == Guid.Empty)
            return Invalida("Informe Idempotency-Key em formato UUID.");
        var resposta = await executar(autor, chave);
        return resposta.Status switch
        {
            VendaExternaStatus.Sucesso when criar => Results.Created($"{Rota}/{resposta.Venda!.Id:D}", resposta.Venda),
            VendaExternaStatus.Sucesso or VendaExternaStatus.Repetida => Results.Ok(resposta.Venda),
            VendaExternaStatus.Ausente => Results.NotFound(),
            VendaExternaStatus.Conflito => Results.Problem(statusCode: 409, detail: resposta.Erro),
            _ => Invalida(resposta.Erro ?? "Confira os dados da venda."),
        };
    }

    private static bool Autor(HttpContext ctx, out Guid autor) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out autor) && autor != Guid.Empty;

    private static ValidationProblem Invalida(string mensagem) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["venda"] = [mensagem] });
}
