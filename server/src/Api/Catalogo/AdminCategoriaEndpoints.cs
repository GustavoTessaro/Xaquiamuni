using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Catalogo;

public static class AdminCategoriaEndpoints
{
    public const string BasePath = "/api/admin/categorias";

    public static IEndpointRouteBuilder MapAdminCategoriaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization(AuthorizationPolicies.Administracao);

        group.MapGet("", ListarAsync)
            .WithName("ListAdminCategories")
            .WithSummary("Lista as categorias administrativas.")
            .WithDescription("Inclui categorias ativas e inativas, ordenadas por exibição e identificador.")
            .Produces<IReadOnlyList<CategoriaResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", ConsultarAsync)
            .WithName("GetAdminCategory")
            .WithSummary("Consulta uma categoria pelo identificador.")
            .Produces<CategoriaResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CadastrarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("CreateAdminCategory")
            .WithSummary("Cadastra uma categoria ativa.")
            .Produces<CategoriaResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", EditarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("UpdateAdminCategory")
            .WithSummary("Edita os dados de uma categoria.")
            .Produces<CategoriaResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}/status", AlterarStatusAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminCategoryStatus")
            .WithSummary("Ativa ou desativa uma categoria.")
            .Produces<CategoriaResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListarAsync(
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        var categorias = await gerenciarCategorias.ListarAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(categorias.Select(CategoriaResponse.From).ToArray());
    }

    private static async Task<IResult> ConsultarAsync(
        Guid id,
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        var categoria = await gerenciarCategorias.ConsultarAsync(id, cancellationToken).ConfigureAwait(false);
        return categoria is null
            ? Results.NotFound()
            : Results.Ok(CategoriaResponse.From(categoria));
    }

    private static async Task<IResult> CadastrarAsync(
        CategoriaWriteRequest? request,
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarCategorias.CadastrarAsync(
            request?.ToInput(),
            cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CategoriaStatus.Success => Results.Created(
                $"{BasePath}/{result.Categoria!.Id}",
                CategoriaResponse.From(result.Categoria)),
            _ => MapFailure(result)
        };
    }

    private static async Task<IResult> EditarAsync(
        Guid id,
        CategoriaWriteRequest? request,
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarCategorias.EditarAsync(
            id,
            request?.ToInput(),
            cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CategoriaStatus.Success => Results.Ok(CategoriaResponse.From(result.Categoria!)),
            _ => MapFailure(result)
        };
    }

    private static async Task<IResult> AlterarStatusAsync(
        Guid id,
        CategoriaStatusRequest? request,
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarCategorias.AlterarStatusAsync(
            id,
            request?.Ativa,
            cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CategoriaStatus.Success => Results.Ok(CategoriaResponse.From(result.Categoria!)),
            _ => MapFailure(result)
        };
    }

    private static IResult MapFailure(CategoriaResult result) =>
        result.Status switch
        {
            CategoriaStatus.ValidationFailed => Results.ValidationProblem(
                result.Erros
                    .GroupBy(erro => erro.Campo, StringComparer.Ordinal)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.Select(erro => erro.Mensagem).ToArray(),
                        StringComparer.Ordinal)),
            CategoriaStatus.NotFound => Results.NotFound(),
            CategoriaStatus.NameAlreadyExists => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Nome de categoria duplicado",
                detail: "Já existe uma categoria com esse nome."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), "Unsupported category result status.")
        };
}

/// <summary>Dados usados para criar ou editar uma categoria.</summary>
public sealed record CategoriaWriteRequest
{
    /// <summary>Nome obrigatório da categoria.</summary>
    public string? Nome { get; init; }

    /// <summary>Descrição opcional da categoria.</summary>
    public string? Descricao { get; init; }

    /// <summary>Ordem de exibição, iniciando em zero.</summary>
    public int? OrdemExibicao { get; init; }

    public CategoriaInput ToInput() => new(Nome, Descricao, OrdemExibicao);
}

/// <summary>Indica se uma categoria deve ficar ativa ou inativa.</summary>
public sealed record CategoriaStatusRequest
{
    /// <summary>Novo estado ativo da categoria.</summary>
    public bool? Ativa { get; init; }
}

/// <summary>Representação pública da categoria para operações administrativas.</summary>
public sealed record CategoriaResponse(
    Guid Id,
    string Nome,
    string? Descricao,
    bool Ativa,
    int OrdemExibicao)
{
    public static CategoriaResponse From(Categoria categoria) =>
        new(categoria.Id, categoria.Nome, categoria.Descricao, categoria.Ativa, categoria.OrdemExibicao);
}
