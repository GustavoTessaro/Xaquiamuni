using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Dashboard;

namespace Xaquiamuni.Api.Dashboard;

public static class AdminDashboardEndpoints
{
    public static void MapAdminDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/api/admin/dashboard").RequireAuthorization(AuthorizationPolicies.Administracao);
        grupo.AddEndpointFilter((contexto, proximo) =>
        {
            contexto.HttpContext.Response.Headers.CacheControl = "no-store";
            return proximo(contexto);
        });
        grupo.MapGet("/operacao", (IConsultarDashboardAdmin dados, CancellationToken ct) => dados.OperacaoAsync(ct));
        grupo.MapGet("/estoque", (IConsultarDashboardAdmin dados, CancellationToken ct) => dados.EstoqueAsync(ct));
        grupo.MapGet("/agenda", (IConsultarDashboardAdmin dados, CancellationToken ct) => dados.AgendaAsync(ct));
    }
}
