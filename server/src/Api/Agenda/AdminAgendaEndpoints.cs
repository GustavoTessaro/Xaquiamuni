using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Agenda;

namespace Xaquiamuni.Api.Agenda;

public static class AdminAgendaEndpoints
{
    private const string Rota = "/api/admin/agenda";

    public static void MapAdminAgendaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup(Rota).RequireAuthorization(AuthorizationPolicies.Administracao);
        grupo.AddEndpointFilter((contexto, proximo) =>
        {
            contexto.HttpContext.Response.Headers.CacheControl = "no-store";
            return proximo(contexto);
        });

        grupo.MapGet("", async Task<IResult> (DateOnly? dataInicial, DateOnly? dataFinal,
            IGerenciarAgenda agenda, CancellationToken ct) =>
            dataInicial > dataFinal ? Invalida("Confira o intervalo de datas.") :
                Results.Ok(await agenda.ListarAsync(dataInicial, dataFinal, ct)));
        grupo.MapGet("/{id:guid}", async Task<IResult> (Guid id, IGerenciarAgenda agenda, CancellationToken ct) =>
            await agenda.ConsultarAsync(id, ct) is { } dados ? Results.Ok(dados) : Results.NotFound());
        grupo.MapGet("/tentativas/{chave:guid}", async Task<IResult> (Guid chave, HttpContext contexto,
            IGerenciarAgenda agenda, CancellationToken ct) =>
        {
            if (!Autor(contexto, out var autor)) return Results.Forbid();
            if (chave == Guid.Empty) return Invalida("Informe uma chave UUID válida.");
            return await agenda.ConsultarTentativaAsync(autor, chave, ct) is { } dados ? Results.Ok(dados) : Results.NotFound();
        });

        grupo.MapPost("", (CompromissoAgendaInput input, HttpContext contexto, IGerenciarAgenda agenda, CancellationToken ct) =>
            OperarAsync(contexto, (autor, chave) => agenda.CriarAsync(input, autor, chave, ct), criar: true))
            .RequireXaquiamuniAntiforgery();
        grupo.MapPut("/{id:guid}", (Guid id, CompromissoAgendaInput input, HttpContext contexto,
            IGerenciarAgenda agenda, CancellationToken ct) =>
            OperarAsync(contexto, (autor, chave) => agenda.EditarAsync(id, input, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();
        grupo.MapDelete("/{id:guid}", (Guid id, Guid versao, HttpContext contexto,
            IGerenciarAgenda agenda, CancellationToken ct) =>
            OperarAsync(contexto, (autor, chave) => agenda.ExcluirAsync(id, versao, autor, chave, ct)))
            .RequireXaquiamuniAntiforgery();
    }

    private static async Task<IResult> OperarAsync(HttpContext contexto, Func<Guid, Guid, Task<ResultadoAgenda>> executar,
        bool criar = false)
    {
        if (!Autor(contexto, out var autor)) return Results.Forbid();
        if (!Guid.TryParseExact(contexto.Request.Headers["Idempotency-Key"], "D", out var chave) || chave == Guid.Empty)
            return Invalida("Informe Idempotency-Key em formato UUID.");
        var resultado = await executar(autor, chave);
        return resultado.Status switch
        {
            StatusAgenda.Sucesso when criar => Results.Created($"{Rota}/{resultado.Dados!.Id:D}", resultado.Dados),
            StatusAgenda.Sucesso or StatusAgenda.Repetida => Results.Ok(resultado.Dados),
            StatusAgenda.Ausente => Results.NotFound(),
            StatusAgenda.Conflito => Results.Problem(statusCode: 409, detail: resultado.Erro),
            _ => Invalida(resultado.Erro ?? "Confira os dados do compromisso.")
        };
    }

    private static bool Autor(HttpContext contexto, out Guid autor) =>
        Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out autor) && autor != Guid.Empty;

    private static IResult Invalida(string mensagem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["agenda"] = [mensagem] });
}
