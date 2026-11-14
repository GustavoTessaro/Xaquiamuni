using System.Security.Claims;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Api.Autorizacao;

namespace Xaquiamuni.Api.Autenticacao;

public static class AdminAuthEndpoints
{
    public const string LoginEndpoint = "/api/admin/auth/login";
    public const string MeEndpoint = "/api/admin/auth/me";
    public const string LogoutEndpoint = "/api/admin/auth/logout";

    public static IEndpointRouteBuilder MapXaquiamuniAdminAuthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LoginEndpoint, LoginAsync)
            .AllowAnonymous()
            .RequireXaquiamuniAntiforgery()
            .WithName("LoginAdministrator")
            .WithSummary("Autentica uma conta administrativa.")
            .WithDescription("Valida as credenciais e autentica somente contas associadas ao papel ADMIN.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet(MeEndpoint, GetCurrentAdministrator)
            .RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithName("GetCurrentAdministrator")
            .WithSummary("Retorna a identidade da sessão administrativa.")
            .WithDescription("Retorna somente o identificador da conta ADMIN autenticada.")
            .Produces<CurrentAdministratorResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        endpoints.MapPost(LogoutEndpoint, (Delegate)AuthEndpoints.LogoutAsync)
            .RequireXaquiamuniAntiforgery()
            .RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithName("LogoutAdministrator")
            .WithSummary("Encerra a sessão administrativa atual.")
            .WithDescription("Remove o mesmo cookie seguro usado pela infraestrutura de autenticação.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        AdminLoginRequest? request,
        ILoginAdministrador loginAdministrador,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrWhiteSpace(request.Senha))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Requisição inválida",
                detail: "Informe e-mail e senha.");
        }

        var result = await loginAdministrador.ExecuteAsync(
            new LoginAdministradorInput
            {
                Email = request.Email,
                Senha = request.Senha
            },
            cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            LoginAdministradorStatus.Succeeded => Results.NoContent(),
            LoginAdministradorStatus.InvalidCredentials => Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Falha na autenticação",
                detail: "Não foi possível autenticar com os dados informados."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                "The administrator login use case returned an unsupported status.")
        };
    }

    private static IResult GetCurrentAdministrator(HttpContext context)
    {
        var userIdValue = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdValue, out var userId)
            ? Results.Ok(new CurrentAdministratorResponse(userId))
            : Results.Unauthorized();
    }
}

/// <summary>Credenciais usadas para autenticar uma conta administrativa.</summary>
public sealed record AdminLoginRequest
{
    /// <summary>E-mail associado à conta administrativa.</summary>
    public string? Email { get; init; }

    /// <summary>Senha da conta administrativa.</summary>
    public string? Senha { get; init; }
}

/// <summary>Identificador público da sessão administrativa autenticada.</summary>
public sealed record CurrentAdministratorResponse(Guid UserId);
