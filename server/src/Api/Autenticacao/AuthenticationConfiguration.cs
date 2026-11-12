using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Api.Autenticacao;

public static class AuthenticationConfiguration
{
    public const string CookieScheme = XaquiamuniIdentityDefaults.AuthenticationScheme;
    public const string CookieName = "__Host-Xaquiamuni.Auth";

    public static IServiceCollection AddXaquiamuniAuthentication(this IServiceCollection services)
    {
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });

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
                options.Events.OnValidatePrincipal = async context =>
                {
                    await SecurityStampValidator.ValidatePrincipalAsync(context)
                        .ConfigureAwait(false);

                    if (context.Principal is not null)
                    {
                        context.ShouldRenew = false;
                    }
                };
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
            })
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
            {
                options.Cookie.Name = "__Host-Xaquiamuni.TwoFactorRememberMe";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = false;
            });

        return services;
    }
}
