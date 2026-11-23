using System.Security.Claims;
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

        group.MapDelete("/{id:guid}", ExcluirAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("DeleteAdminCategory");

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
        HttpContext contexto,
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        var categoria = await gerenciarCategorias.ConsultarAsync(id, cancellationToken).ConfigureAwait(false);
        contexto.Response.Headers.CacheControl = "private, no-store";
        if (categoria is null) return Results.NotFound();
        DefinirVersao(contexto, categoria);
        return Results.Ok(CategoriaResponse.From(categoria));
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
        HttpContext contexto,
        CategoriaWriteRequest? request,
        IGerenciarCategorias gerenciarCategorias,
        CancellationToken cancellationToken)
    {
        if (!LerVersao(contexto, out var versao)) return VersaoObrigatoria();
        var result = await gerenciarCategorias.EditarAsync(
            id,
            request is null ? null : request.ToInput() with { VersaoEsperada = versao },
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

    private static async Task<IResult> ExcluirAsync(Guid id, HttpContext contexto, IExcluirCategoria exclusao, CancellationToken ct)
    {
        contexto.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var autor) || autor == Guid.Empty)
            return Results.Unauthorized();
        if (id == Guid.Empty) return Problema(400, "categoria_invalida", "Informe uma categoria válida.");
        if (!LerVersao(contexto, out var versao)) return VersaoObrigatoria();
        var chaves = contexto.Request.Headers["Idempotency-Key"];
        if (chaves.Count != 1 || !Guid.TryParseExact(chaves[0], "D", out var chave) || chave == Guid.Empty)
            return Problema(400, "chave_invalida", "Informe uma Idempotency-Key UUID válida.");

        return await exclusao.ExecutarAsync(id, versao, autor, chave, ct) switch
        {
            ExclusaoCategoriaStatus.Excluida => Results.NoContent(),
            ExclusaoCategoriaStatus.Ausente => Results.NotFound(),
            ExclusaoCategoriaStatus.ComProdutos => Problema(409, "categoria_com_produtos", "Esta categoria possui produtos vinculados."),
            ExclusaoCategoriaStatus.VersaoDesatualizada => ConflitoVersao(),
            ExclusaoCategoriaStatus.ChaveEmUso => Problema(409, "chave_em_uso", "Esta chave já foi usada com outros parâmetros."),
            _ => Problema(409, "conflito_concorrencia", "O cadastro está sendo alterado. Recarregue antes de tentar novamente.")
        };
    }

    private static bool LerVersao(HttpContext contexto, out Guid versao)
    {
        versao = Guid.Empty;
        var tags = contexto.Request.Headers.IfMatch;
        return tags.Count == 1 && tags[0] is { Length: 38 } tag && tag[0] == '"' && tag[^1] == '"' &&
            Guid.TryParseExact(tag.AsSpan(1, 36), "D", out versao) && versao != Guid.Empty;
    }

    private static void DefinirVersao(HttpContext contexto, Categoria categoria) =>
        contexto.Response.Headers.ETag = $"\"{categoria.Versao:D}\"";

    private static IResult VersaoObrigatoria() => Problema(400, "versao_obrigatoria", "Informe a versão da categoria em If-Match.");
    private static IResult ConflitoVersao() => Problema(409, "versao_desatualizada", "A categoria mudou. Recarregue os dados.");
    private static IResult Problema(int status, string codigo, string detalhe) =>
        Results.Problem(statusCode: status, detail: detalhe, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

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
            CategoriaStatus.VersionConflict => ConflitoVersao(),
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
    int OrdemExibicao, Guid Versao)
{
    public static CategoriaResponse From(Categoria categoria) =>
        new(categoria.Id, categoria.Nome, categoria.Descricao, categoria.Ativa, categoria.OrdemExibicao, categoria.Versao);
}
