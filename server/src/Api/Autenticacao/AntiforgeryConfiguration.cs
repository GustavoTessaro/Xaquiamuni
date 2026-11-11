using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Xaquiamuni.Api.Autenticacao;

public static class AntiforgeryConfiguration
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string CookieName = "XSRF-TOKEN";
    public const string TokenEndpoint = "/api/auth/csrf";

    public static IServiceCollection AddXaquiamuniAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
        });

        return services;
    }

    public static IEndpointRouteBuilder MapXaquiamuniCsrfEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(TokenEndpoint, (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        });

        return endpoints;
    }

    public static RouteHandlerBuilder RequireXaquiamuniAntiforgery(this RouteHandlerBuilder endpoint)
    {
        return endpoint.AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest();
            }

            return await next(context);
        });
    }
}
