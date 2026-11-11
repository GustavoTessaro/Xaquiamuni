using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Api.Autenticacao;

public static class AuthenticationConfiguration
{
    public const string CookieScheme = XaquiamuniIdentityDefaults.AuthenticationScheme;
    public const string CookieName = "__Host-Xaquiamuni.Auth";

    public static IServiceCollection AddXaquiamuniAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieScheme)
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        return services;
    }
}
