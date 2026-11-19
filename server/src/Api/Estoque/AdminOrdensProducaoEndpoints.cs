using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Api.Estoque;

public static class AdminOrdensProducaoEndpoints
{
    private const string Rota = "/api/admin/producao";

    public static void MapAdminOrdensProducaoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Rota).RequireAuthorization(AuthorizationPolicies.Administracao);
        group.MapGet("", ListarAsync);
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, IGerenciarOrdensProducao ordens, CancellationToken ct) =>
            await ordens.ConsultarAsync(id, ct) is { } ordem ? Results.Ok(ordem) : Results.NotFound());
        group.MapGet("/previa", async (Guid receitaId, int quantidade, IGerenciarOrdensProducao ordens, CancellationToken ct) =>
        {
            var resultado = await ordens.PreverAsync(receitaId, quantidade, ct);
            return resultado.Status == OperacaoProducaoStatus.Sucesso ? Results.Ok(resultado.Previa) : Falha(resultado);
        });
        group.MapGet("/tentativas/{chave:guid}", async Task<IResult> (Guid chave, HttpContext ctx, IGerenciarOrdensProducao ordens, CancellationToken ct) =>
        {
            if (!Identificar(ctx, out var autor)) return Results.Forbid();
            var ordem = await ordens.ConsultarTentativaAsync(autor, chave, ct);
            return ordem.HasValue ? Results.Ok(new { ordemId = ordem.Value }) : Results.NotFound();
        });
        group.MapPost("", async Task<IResult> (InicioProducaoInput input, HttpContext ctx, IGerenciarOrdensProducao ordens, CancellationToken ct) =>
        {
            if (!Identificar(ctx, out var autor)) return Results.Forbid();
            if (!Chave(ctx, out var chave)) return Invalida("Informe Idempotency-Key em formato UUID.");
            var resultado = await ordens.IniciarAsync(input, autor, chave, ct);
            return resultado.Status switch
            {
                OperacaoProducaoStatus.Sucesso => Results.Created($"{Rota}/{resultado.OrdemId:D}", new { ordemId = resultado.OrdemId }),
                OperacaoProducaoStatus.Repetida => Results.Ok(new { ordemId = resultado.OrdemId }),
                _ => Falha(resultado)
            };
        }).RequireXaquiamuniAntiforgery();
        group.MapPost("/{id:guid}/concluir", ConcluirAsync).RequireXaquiamuniAntiforgery();
        group.MapPost("/{id:guid}/cancelar", async Task<IResult> (Guid id, HttpContext ctx, IGerenciarOrdensProducao ordens, CancellationToken ct) =>
        {
            if (!Identificar(ctx, out var autor)) return Results.Forbid();
            if (!Chave(ctx, out var chave)) return Invalida("Informe Idempotency-Key em formato UUID.");
            var resultado = await ordens.CancelarAsync(id, autor, chave, ct);
            return resultado.Status is OperacaoProducaoStatus.Sucesso or OperacaoProducaoStatus.Repetida
                ? Results.Ok(new { ordemId = resultado.OrdemId }) : Falha(resultado);
        }).RequireXaquiamuniAntiforgery();
    }

    private static async Task<IResult> ConcluirAsync(Guid id, ConcluirProducaoInput input, HttpContext ctx,
        IConcluirOrdemProducao conclusao, CancellationToken ct)
    {
        if (!Identificar(ctx, out var autor)) return Results.Forbid();
        if (!Chave(ctx, out var chave)) return Invalida("Informe Idempotency-Key em formato UUID.");
        var resultado = await conclusao.ConcluirAsync(id, autor, chave, input, ct);
        return resultado.Status is OperacaoProducaoStatus.Sucesso or OperacaoProducaoStatus.Repetida
            ? Results.Ok(await conclusao.ConsultarConclusaoAsync(id, ct)) : Falha(resultado);
    }

    private static async Task<IResult> ListarAsync(string? situacao, Guid? produtoId, Guid? receitaId,
        IGerenciarOrdensProducao ordens, CancellationToken ct)
    {
        SituacaoOrdemProducao? filtro = null;
        if (situacao is not null)
        {
            if (!Enum.TryParse<SituacaoOrdemProducao>(situacao, out var valor) || valor.ToString() != situacao)
                return Invalida("Informe uma situação válida.");
            filtro = valor;
        }
        return Results.Ok(await ordens.ListarAsync(filtro, produtoId, receitaId, ct));
    }

    private static IResult Falha(OperacaoProducaoResult resultado) => resultado.Status switch
    {
        OperacaoProducaoStatus.Ausente => Results.NotFound(),
        OperacaoProducaoStatus.Conflito => Results.Problem(statusCode: 409, detail: resultado.Erro,
            extensions: new Dictionary<string, object?> { ["rejeicaoDefinitiva"] = resultado.RejeicaoDefinitiva }),
        _ => Invalida(resultado.Erro ?? "Confira os dados da produção.")
    };

    private static IResult Invalida(string erro) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["producao"] = [erro] });
    private static bool Identificar(HttpContext ctx, out Guid id) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;
    private static bool Chave(HttpContext ctx, out Guid chave) =>
        Guid.TryParse(ctx.Request.Headers["Idempotency-Key"].ToString(), out chave) && chave != Guid.Empty;
}
