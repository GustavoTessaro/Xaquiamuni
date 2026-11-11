using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests;

public class AuthorizationPolicyTests
{
    private static ServiceProvider CreateAuthorizationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXaquiamuniAuthorization();

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(string? role)
    {
        var claims = role is null
            ? Array.Empty<Claim>()
            : [new Claim(ClaimTypes.Role, role)];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public async Task Administrador_IsAuthorizedForAdministrativeAndOperationalPolicies()
    {
        using var services = CreateAuthorizationServices();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = CreateUser(XaquiamuniRoles.Administrador);

        Assert.True((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.Administracao)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.OperacaoAutorizada)).Succeeded);
    }

    [Fact]
    public async Task Funcionario_IsNotAuthorizedForAdministrativeOrOperationalPoliciesByDefault()
    {
        using var services = CreateAuthorizationServices();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = CreateUser(XaquiamuniRoles.Funcionario);

        Assert.False((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.Administracao)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.OperacaoAutorizada)).Succeeded);
    }

    [Fact]
    public async Task Cliente_IsNotAuthorizedForAdministrativePolicies()
    {
        using var services = CreateAuthorizationServices();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = CreateUser(XaquiamuniRoles.Cliente);

        Assert.False((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.Administracao)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.OperacaoAutorizada)).Succeeded);
    }

    [Fact]
    public async Task ClienteRole_IsAuthorizedForAuthenticatedCustomerPolicy()
    {
        using var services = CreateAuthorizationServices();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = CreateUser(XaquiamuniRoles.Cliente);

        Assert.True((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.ClienteAutenticado)).Succeeded);
    }

    [Fact]
    public async Task UnauthenticatedUserWithClienteRole_IsNotAuthorizedForCustomerPolicy()
    {
        using var services = CreateAuthorizationServices();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, XaquiamuniRoles.Cliente)]);
        var user = new ClaimsPrincipal(identity);

        Assert.False((await authorization.AuthorizeAsync(user, null, AuthorizationPolicies.ClienteAutenticado)).Succeeded);
    }
}
