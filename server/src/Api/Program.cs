using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Infraestrutura;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddXaquiamuniAuthentication();
builder.Services.AddXaquiamuniAntiforgery();
builder.Services.AddXaquiamuniAuthorization();
builder.Services.AddResendConfirmationEmailRateLimiting();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    var isAuthenticationWrite =
        HttpMethods.IsPost(context.Request.Method)
        && (string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.RegisterEndpoint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.LoginEndpoint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.ConfirmEmailEndpoint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.ResendConfirmationEmailEndpoint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.ForgotPasswordEndpoint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.ResetPasswordEndpoint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.Request.Path.Value,
                AuthEndpoints.LogoutEndpoint,
                StringComparison.OrdinalIgnoreCase));
    var isCurrentClientRequest =
        HttpMethods.IsGet(context.Request.Method)
        && string.Equals(
            context.Request.Path.Value,
            AuthEndpoints.MeEndpoint,
            StringComparison.OrdinalIgnoreCase);

    if (isAuthenticationWrite || isCurrentClientRequest)
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next(context);
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapXaquiamuniCsrfEndpoint();
app.MapXaquiamuniAuthEndpoints();

app.Run();

public partial class Program;
