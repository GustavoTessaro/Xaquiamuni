using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Api.Autorizacao;

namespace Xaquiamuni.Api.Autenticacao;

public static class AuthEndpoints
{
    public const string RegisterEndpoint = "/api/auth/register";
    public const string ConfirmEmailEndpoint = "/api/auth/confirm-email";
    public const string ResendConfirmationEmailEndpoint = "/api/auth/resend-confirmation-email";
    public const string LoginEndpoint = "/api/auth/login";
    public const string MeEndpoint = "/api/auth/me";
    public const string LogoutEndpoint = "/api/auth/logout";

    public static IEndpointRouteBuilder MapXaquiamuniAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(RegisterEndpoint, RegisterAsync)
            .AllowAnonymous()
            .RequireXaquiamuniAntiforgery()
            .WithName("RegisterClient")
            .WithSummary("Registra uma nova conta de cliente.")
            .WithDescription("Cria uma conta pública de cliente sem autenticar a sessão.")
            .Produces<RegisterClientResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(ConfirmEmailEndpoint, ConfirmEmailAsync)
            .AllowAnonymous()
            .RequireXaquiamuniAntiforgery()
            .WithName("ConfirmClientEmail")
            .WithSummary("Confirma o e-mail de uma conta de cliente.")
            .WithDescription("Confirma o e-mail usando o token recebido pelo cliente.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(ResendConfirmationEmailEndpoint, ResendConfirmationEmailAsync)
            .AllowAnonymous()
            .RequireXaquiamuniAntiforgery()
            .WithName("ResendClientEmailConfirmation")
            .WithSummary("Solicita o reenvio da confirmação de e-mail.")
            .WithDescription("Aceita a solicitação sem revelar se uma conta é elegível.")
            .Produces<ResendConfirmationEmailResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(LoginEndpoint, LoginAsync)
            .AllowAnonymous()
            .RequireXaquiamuniAntiforgery()
            .WithName("LoginClient")
            .WithSummary("Autentica uma conta de cliente.")
            .WithDescription("Autentica clientes sem revelar se uma conta existe.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet(MeEndpoint, GetCurrentClient)
            .RequireAuthorization(AuthorizationPolicies.ClienteAutenticado)
            .WithName("GetCurrentClient")
            .WithSummary("Retorna a identidade da sessão de cliente.")
            .WithDescription("Retorna somente o identificador do cliente autenticado.")
            .Produces<CurrentClientResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        endpoints.MapPost(LogoutEndpoint, (Delegate)LogoutAsync)
            .RequireXaquiamuniAntiforgery()
            .RequireAuthorization(AuthorizationPolicies.ClienteAutenticado)
            .WithName("LogoutClient")
            .WithSummary("Encerra a sessão de cliente atual.")
            .WithDescription("Remove o cookie de autenticação da sessão atual do navegador.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(AuthenticationConfiguration.CookieScheme).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult GetCurrentClient(HttpContext context)
    {
        var userIdValue = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdValue, out var userId)
            ? Results.Ok(new CurrentClientResponse(userId))
            : Results.Unauthorized();
    }

    private static async Task<IResult> LoginAsync(
        LoginClientRequest? request,
        ILoginCliente loginCliente,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrWhiteSpace(request.Senha))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Requisição inválida",
                detail: "Informe e-mail e senha.");
        }

        var result = await loginCliente.ExecuteAsync(
            new LoginClienteInput
            {
                Email = request.Email,
                Senha = request.Senha
            },
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? Results.NoContent()
            : Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Falha na autenticação",
                detail: "Não foi possível autenticar com os dados informados.");
    }

    private static async Task<IResult> ResendConfirmationEmailAsync(
        ResendConfirmationEmailRequest? request,
        ISolicitarReenvioConfirmacaoEmail solicitarReenvio,
        CancellationToken cancellationToken)
    {
        var result = await solicitarReenvio
            .ExecuteAsync(request?.Email, cancellationToken)
            .ConfigureAwait(false);

        return result.Status switch
        {
            SolicitarReenvioConfirmacaoEmailStatus.InvalidEmail => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Email"] = ["O e-mail informado é inválido."]
                },
                title: "Requisição inválida",
                statusCode: StatusCodes.Status400BadRequest),
            SolicitarReenvioConfirmacaoEmailStatus.RequestProcessed =>
                Results.Accepted(value: new ResendConfirmationEmailResponse(
                    "Se houver uma conta elegível, enviaremos um e-mail de confirmação.")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                "The email confirmation resend use case returned an unsupported result.")
        };
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmClientEmailRequest? request,
        IEmailConfirmationService emailConfirmationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Email)
            || request.Email.Length > ConfirmClientEmailRequest.EmailMaxLength
            || !ConfirmClientEmailRequest.IsValidEmail(request.Email)
            || string.IsNullOrWhiteSpace(request.Token)
            || request.Token.Length > ConfirmClientEmailRequest.TokenMaxLength)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Request"] = ["Informe um e-mail e um token de confirmação válidos."]
                },
                title: "Requisição inválida",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await emailConfirmationService.ConfirmEmailAsync(
                request.Email.Trim(),
                request.Token,
                cancellationToken).ConfigureAwait(false);

            return result.Succeeded
                ? Results.NoContent()
                : EmailConfirmationNotCompleted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Confirmação não concluída",
                detail: "Não foi possível confirmar o e-mail neste momento.");
        }
    }

    private static IResult EmailConfirmationNotCompleted() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Confirmação não concluída",
            detail: "Não foi possível confirmar o e-mail. Verifique o link e tente novamente.");

    private static async Task<IResult> RegisterAsync(
        RegisterClientRequest request,
        ICadastrarCliente cadastrarCliente,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var input = new CadastroClienteInput
        {
            NomeCompleto = request.NomeCompleto,
            Cpf = request.Cpf,
            Email = request.Email,
            Telefone = request.Telefone,
            Senha = request.Senha,
            EnderecoInicial = request.EnderecoInicial is null
                ? null
                : new EnderecoInicialInput
                {
                    Cep = request.EnderecoInicial.Cep,
                    Uf = request.EnderecoInicial.Uf,
                    Cidade = request.EnderecoInicial.Cidade,
                    Bairro = request.EnderecoInicial.Bairro,
                    Logradouro = request.EnderecoInicial.Logradouro,
                    Numero = request.EnderecoInicial.Numero,
                    Complemento = request.EnderecoInicial.Complemento
                }
        };

        CadastroClienteResult result;
        try
        {
            result = await cadastrarCliente.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var hasFailureContext = CadastroClienteDiagnostics.TryGet(
                exception,
                out var failureCategory,
                out var failureStage);
            return RegistrationFailure(
                logger,
                httpContext,
                hasFailureContext
                    ? failureCategory
                    : CadastroClienteFailureCategory.CONFIGURATION_INFRASTRUCTURE,
                hasFailureContext
                    ? failureStage
                    : CadastroClienteFailureStage.USE_CASE_ENTRY,
                exception);
        }

        return result.Status switch
        {
            CadastroClienteStatus.Success when result.UserId is Guid userId =>
                Results.Created($"/api/clientes/{userId}", new RegisterClientResponse(userId)),
            CadastroClienteStatus.ValidationFailed => Results.ValidationProblem(
                result.ValidationErrors
                    .GroupBy(error => error.Campo, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Mensagem).ToArray(),
                        StringComparer.Ordinal),
                statusCode: StatusCodes.Status400BadRequest),
            CadastroClienteStatus.PasswordValidationFailed => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Senha"] = ["A senha não atende aos requisitos de segurança."]
                },
                title: "Dados inválidos",
                statusCode: StatusCodes.Status400BadRequest),
            CadastroClienteStatus.EmailAlreadyRegistered or CadastroClienteStatus.CpfAlreadyRegistered =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Cadastro não concluído",
                    detail: "Não foi possível concluir o cadastro com os dados informados."),
            _ => RegistrationFailure(
                logger,
                httpContext,
                result.FailureCategory ?? CadastroClienteFailureCategory.CONFIGURATION_INFRASTRUCTURE,
                result.FailureStage ?? CadastroClienteFailureStage.USE_CASE_ENTRY,
                null,
                result.SafeDiagnosticCodes)
        };
    }

    private static IResult RegistrationFailure(
        ILogger<Program> logger,
        HttpContext httpContext,
        CadastroClienteFailureCategory category,
        CadastroClienteFailureStage stage,
        Exception? exception,
        IReadOnlyList<string>? safeDiagnosticCodes = null)
    {
        logger.LogError(
            "Client registration could not be completed. Category={FailureCategory} Stage={FailureStage} ExceptionType={ExceptionType} IdentityErrorCodes={IdentityErrorCodes} TraceId={TraceId}",
            category,
            stage,
            GetSafeExceptionType(exception),
            safeDiagnosticCodes is { Count: > 0 }
                ? string.Join(
                    ',',
                    CadastroClienteDiagnostics.SanitizeIdentityErrorCodes(safeDiagnosticCodes))
                : "NONE",
            httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Falha no cadastro",
            detail: "Não foi possível concluir o cadastro neste momento.");
    }

    private static string GetSafeExceptionType(Exception? exception) =>
        exception switch
        {
            null => "NONE",
            InvalidOperationException => "INVALID_OPERATION",
            ArgumentException => "ARGUMENT",
            System.Security.Cryptography.CryptographicException => "CRYPTOGRAPHIC",
            _ => "OTHER"
        };
}

/// <summary>Credenciais públicas usadas para autenticar uma conta de cliente.</summary>
public sealed record LoginClientRequest
{
    /// <summary>E-mail associado à conta.</summary>
    public string? Email { get; init; }

    /// <summary>Senha da conta.</summary>
    public string? Senha { get; init; }
}

/// <summary>Identificador público da sessão de cliente autenticada.</summary>
public sealed record CurrentClientResponse(Guid UserId);

/// <summary>Dados públicos necessários para solicitar o cadastro de um cliente.</summary>
public sealed class RegisterClientRequest
{
    public string? NomeCompleto { get; init; }
    public string? Cpf { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? Senha { get; init; }
    public RegisterClientAddressRequest? EnderecoInicial { get; init; }
}

/// <summary>Endereço inicial informado durante o cadastro do cliente.</summary>
public sealed class RegisterClientAddressRequest
{
    public string? Cep { get; init; }
    public string? Uf { get; init; }
    public string? Cidade { get; init; }
    public string? Bairro { get; init; }
    public string? Logradouro { get; init; }
    public string? Numero { get; init; }
    public string? Complemento { get; init; }
}

/// <summary>Dados necessários para confirmar o e-mail de uma conta de cliente.</summary>
public sealed record ConfirmClientEmailRequest
{
    public const int EmailMaxLength = 256;
    public const int TokenMaxLength = 4096;

    public string? Email { get; init; }
    public string? Token { get; init; }

    public static bool IsValidEmail(string email)
    {
        var value = email.Trim();
        var separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var domain = value.AsSpan(separator + 1);
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}

/// <summary>Dados públicos para solicitar o reenvio da confirmação de e-mail.</summary>
public sealed record ResendConfirmationEmailRequest
{
    public string? Email { get; init; }
}

/// <summary>Resposta genérica à solicitação de reenvio da confirmação de e-mail.</summary>
public sealed record ResendConfirmationEmailResponse(string Message);

/// <summary>Identificador público da conta de cliente criada.</summary>
public sealed record RegisterClientResponse(Guid UserId);
