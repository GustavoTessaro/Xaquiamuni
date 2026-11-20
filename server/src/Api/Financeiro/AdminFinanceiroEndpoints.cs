using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Financeiro;

namespace Xaquiamuni.Api.Financeiro;

public static class AdminFinanceiroEndpoints
{
    private const string Rota = "/api/admin/financeiro";

    public static void MapAdminFinanceiroEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup(Rota).RequireAuthorization(AuthorizationPolicies.Administracao);
        grupo.AddEndpointFilter((ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = "no-store";
            return next(ctx);
        });

        grupo.MapGet("/lancamentos", async Task<IResult> (int? pagina, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
            PaginaValida(pagina)
                ? Results.Ok(await financeiro.ListarAsync(pagina ?? 1, ct))
                : Invalida("Informe uma página entre 1 e 10000."));
        grupo.MapGet("/lancamentos/{id:guid}", async Task<IResult> (Guid id, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
            await financeiro.ConsultarAsync(id, ct) is { } dados ? Results.Ok(dados) : Results.NotFound());
        grupo.MapPost("/lancamentos", (CriarLancamentoInput input, HttpContext ctx, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
            OperarAsync(ctx, (autor, chave) => financeiro.CriarAsync(input, autor, chave, ct),
                dados => $"{Rota}/lancamentos/{dados.Id:D}"))
            .RequireXaquiamuniAntiforgery();
        grupo.MapPost("/lancamentos/{id:guid}/cancelar",
            (Guid id, CancelarLancamentoInput input, HttpContext ctx, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
                OperarAsync(ctx, (autor, chave) => financeiro.CancelarAsync(id, input, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();

        grupo.MapGet("/compras/{id:guid}", async Task<IResult> (Guid id, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
            await financeiro.ConsultarCompraAsync(id, ct) is { } dados ? Results.Ok(dados) : Results.NotFound());
        grupo.MapGet("/compras/{id:guid}/pagamentos",
            async Task<IResult> (Guid id, int? pagina, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
            {
                if (!PaginaValida(pagina)) return Invalida("Informe uma página entre 1 e 10000.");
                return await financeiro.ListarPagamentosAsync(id, pagina ?? 1, ct) is { } dados
                    ? Results.Ok(dados) : Results.NotFound();
            });
        grupo.MapPost("/compras/{id:guid}/pagamentos",
            (Guid id, PagarFornecedorInput input, HttpContext ctx, IGerenciarFinanceiro financeiro, CancellationToken ct) =>
                OperarAsync(ctx, (autor, chave) => financeiro.PagarAsync(id, input, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();
    }

    private static bool PaginaValida(int? pagina) => pagina is null or (>= 1 and <= 10000);

    private static async Task<IResult> OperarAsync<T>(HttpContext ctx, Func<Guid, Guid, Task<ResultadoFinanceiro<T>>> executar,
        Func<T, string>? local = null)
    {
        if (!Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var autor) || autor == Guid.Empty)
            return Results.Forbid();
        if (!Guid.TryParseExact(ctx.Request.Headers["Idempotency-Key"], "D", out var chave) || chave == Guid.Empty)
            return Invalida("Informe Idempotency-Key em formato UUID.");
        var resultado = await executar(autor, chave);
        return resultado.Status switch
        {
            StatusFinanceiro.Sucesso when local is not null => Results.Created(local(resultado.Dados!), resultado.Dados),
            StatusFinanceiro.Sucesso or StatusFinanceiro.Repetida => Results.Ok(resultado.Dados),
            StatusFinanceiro.Ausente => Results.NotFound(),
            StatusFinanceiro.Conflito => Results.Problem(statusCode: 409, detail: resultado.Erro),
            _ => Invalida(resultado.Erro ?? "Confira os dados financeiros.")
        };
    }

    private static IResult Invalida(string mensagem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["financeiro"] = [mensagem] });
}
