using Xaquiamuni.Aplicacao.Cadastro;

namespace Xaquiamuni.Api.Autenticacao;

public static class AuthEndpoints
{
    public const string RegisterEndpoint = "/api/auth/register";

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

        return endpoints;
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
