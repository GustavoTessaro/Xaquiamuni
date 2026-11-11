using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Api.Autorizacao;

public static class AuthorizationPolicies
{
    public const string Administracao = "Administracao";
    public const string OperacaoAutorizada = "OperacaoAutorizada";
    public const string ClienteAutenticado = "ClienteAutenticado";
}

public static class AuthorizationConfiguration
{
    public static IServiceCollection AddXaquiamuniAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Administracao, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(XaquiamuniRoles.Administrador))
            .AddPolicy(AuthorizationPolicies.OperacaoAutorizada, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(XaquiamuniRoles.Administrador))
            .AddPolicy(AuthorizationPolicies.ClienteAutenticado, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(XaquiamuniRoles.Cliente));

        return services;
    }
}
