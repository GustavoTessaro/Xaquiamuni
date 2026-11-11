using System.Security.Claims;
using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Api.Autorizacao;

namespace Xaquiamuni.Api.Autenticacao;

public static class AuthEndpoints
{
    public const string RegisterEndpoint = "/api/auth/register";
    public const string LoginEndpoint = "/api/auth/login";
    public const string MeEndpoint = "/api/auth/me";

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

        return endpoints;
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

    private static async Task<IResult> RegisterAsync(
        RegisterClientRequest request,
        ICadastrarCliente cadastrarCliente,
        ILogger<Program> logger,
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
        catch (Exception)
        {
            logger.LogError("Unexpected error while processing client registration.");
            return RegistrationFailure();
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
            CadastroClienteStatus.EmailAlreadyRegistered or CadastroClienteStatus.CpfAlreadyRegistered =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Cadastro não concluído",
                    detail: "Não foi possível concluir o cadastro com os dados informados."),
            _ => RegistrationFailure()
        };

        IResult RegistrationFailure()
        {
            logger.LogError("Client registration could not be completed.");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Falha no cadastro",
                detail: "Não foi possível concluir o cadastro neste momento.");
        }
    }
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

/// <summary>Identificador público da conta de cliente criada.</summary>
public sealed record RegisterClientResponse(Guid UserId);
