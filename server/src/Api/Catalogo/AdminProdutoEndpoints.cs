using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Catalogo;

public static class AdminProdutoEndpoints
{
    public const string BasePath = "/api/admin/produtos";

    public static IEndpointRouteBuilder MapAdminProdutoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithTags("Produtos administrativos");

        group.MapGet("", ListarAsync)
            .WithName("ListAdminProducts")
            .WithSummary("Lista produtos administrativos.")
            .WithDescription("Inclui produtos publicados e não publicados e suas variantes.")
            .Produces<IReadOnlyList<ProdutoResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", ConsultarAsync)
            .WithName("GetAdminProduct")
            .WithSummary("Consulta um produto e suas variantes.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CadastrarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("CreateAdminProduct")
            .WithSummary("Cadastra um produto não publicado com variante padrão.")
            .Produces<ProdutoResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", EditarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("UpdateAdminProduct")
            .WithSummary("Edita nome, descrição e categoria de um produto.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/variantes", AdicionarVarianteAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("AddAdminProductVariant")
            .WithSummary("Adiciona uma variante ativa não padrão.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/variantes/{varianteId:guid}", EditarVarianteAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("UpdateAdminProductVariant")
            .WithSummary("Edita identificação, unidade de venda e preço da variante.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/variantes/{varianteId:guid}/padrao", DefinirVariantePadraoAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminProductDefaultVariant")
            .WithSummary("Define uma variante ativa como padrão.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}/variantes/{varianteId:guid}/status", AlterarStatusVarianteAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminProductVariantStatus")
            .WithSummary("Ativa ou desativa uma variante.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}/publicacao", AlterarPublicacaoAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminProductPublication")
            .WithSummary("Publica ou despublica um produto.")
            .Produces<ProdutoResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListarAsync(
        string? busca,
        Guid? categoriaId,
        bool? publicado,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var produtos = await gerenciarProdutos.ListarAsync(
            new ProdutoFiltro(busca, categoriaId, publicado),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(produtos.Select(ProdutoResponse.From).ToArray());
    }

    private static async Task<IResult> ConsultarAsync(
        Guid id,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var produto = await gerenciarProdutos.ConsultarAsync(id, cancellationToken).ConfigureAwait(false);
        return produto is null
            ? NotFound("Produto não encontrado.")
            : Results.Ok(ProdutoResponse.From(produto));
    }

    private static async Task<IResult> CadastrarAsync(
        ProdutoCadastroRequest? request,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.CadastrarAsync(
            request?.ToInput(),
            cancellationToken).ConfigureAwait(false);

        return result.Status == ProdutoStatus.Success
            ? Results.Created(
                $"{BasePath}/{result.Produto!.Id}",
                ProdutoResponse.From(result.Produto))
            : MapFailure(result);
    }

    private static async Task<IResult> EditarAsync(
        Guid id,
        ProdutoEdicaoRequest? request,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.EditarAsync(
            id,
            request?.ToInput(),
            cancellationToken).ConfigureAwait(false);
        return result.Status == ProdutoStatus.Success
            ? Results.Ok(ProdutoResponse.From(result.Produto!))
            : MapFailure(result);
    }

    private static async Task<IResult> AdicionarVarianteAsync(
        Guid id,
        ProdutoVarianteRequest? request,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.AdicionarVarianteAsync(
            id,
            request?.ToInput(),
            cancellationToken).ConfigureAwait(false);
        return result.Status == ProdutoStatus.Success
            ? Results.Ok(ProdutoResponse.From(result.Produto!))
            : MapFailure(result);
    }

    private static async Task<IResult> EditarVarianteAsync(
        Guid id,
        Guid varianteId,
        ProdutoVarianteRequest? request,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.EditarVarianteAsync(
            id,
            varianteId,
            request?.ToInput(),
            cancellationToken).ConfigureAwait(false);
        return result.Status == ProdutoStatus.Success
            ? Results.Ok(ProdutoResponse.From(result.Produto!))
            : MapFailure(result);
    }

    private static async Task<IResult> DefinirVariantePadraoAsync(
        Guid id,
        Guid varianteId,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.DefinirVariantePadraoAsync(
            id,
            varianteId,
            cancellationToken).ConfigureAwait(false);
        return result.Status == ProdutoStatus.Success
            ? Results.Ok(ProdutoResponse.From(result.Produto!))
            : MapFailure(result);
    }

    private static async Task<IResult> AlterarStatusVarianteAsync(
        Guid id,
        Guid varianteId,
        ProdutoVarianteStatusRequest? request,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.AlterarStatusVarianteAsync(
            id,
            varianteId,
            request?.Ativa,
            request?.NovoPadraoId,
            cancellationToken).ConfigureAwait(false);
        return result.Status == ProdutoStatus.Success
            ? Results.Ok(ProdutoResponse.From(result.Produto!))
            : MapFailure(result);
    }

    private static async Task<IResult> AlterarPublicacaoAsync(
        Guid id,
        ProdutoPublicacaoRequest? request,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarProdutos.AlterarPublicacaoAsync(
            id,
            request?.Publicado,
            cancellationToken).ConfigureAwait(false);
        return result.Status == ProdutoStatus.Success
            ? Results.Ok(ProdutoResponse.From(result.Produto!))
            : MapFailure(result);
    }

    private static IResult MapFailure(ProdutoResult result) =>
        result.Status switch
        {
            ProdutoStatus.ValidationFailed => Results.ValidationProblem(
                result.Erros
                    .GroupBy(erro => erro.Campo, StringComparer.Ordinal)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.Select(erro => erro.Mensagem).ToArray(),
                        StringComparer.Ordinal)),
            ProdutoStatus.ProductNotFound => NotFound("Produto não encontrado."),
            ProdutoStatus.CategoryNotFound => NotFound("Categoria não encontrada."),
            ProdutoStatus.VariantNotFound => NotFound("Variante não encontrada neste produto."),
            ProdutoStatus.CategoryInactive => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Categoria inativa",
                detail: result.Mensagem),
            ProdutoStatus.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflito no produto",
                detail: result.Mensagem),
            _ => throw new ArgumentOutOfRangeException(nameof(result), "Unsupported product result status.")
        };

    private static IResult NotFound(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Recurso não encontrado",
            detail: detail);
}

/// <summary>Payload para cadastro de produto e sua variante padrão obrigatória.</summary>
public sealed record ProdutoCadastroRequest
{
    /// <summary>Nome do produto.</summary>
    public string? Nome { get; init; }

    /// <summary>Descrição opcional do produto.</summary>
    public string? Descricao { get; init; }

    /// <summary>Identificador da categoria ativa.</summary>
    public Guid? CategoriaId { get; init; }

    /// <summary>Dados da variante padrão ativa.</summary>
    public ProdutoVarianteRequest? VariantePadrao { get; init; }

    /// <summary>Variantes adicionais, criadas ativas e não padrão.</summary>
    public IReadOnlyList<ProdutoVarianteRequest?>? Variantes { get; init; }

    public ProdutoCadastroInput ToInput() =>
        new(Nome, Descricao, CategoriaId, VariantePadrao?.ToInput(), Variantes?.Select(item => item?.ToInput()).ToArray());
}

/// <summary>Payload para edição dos dados principais do produto.</summary>
public sealed record ProdutoEdicaoRequest
{
    /// <summary>Nome do produto.</summary>
    public string? Nome { get; init; }

    /// <summary>Descrição opcional do produto.</summary>
    public string? Descricao { get; init; }

    /// <summary>Categoria desejada para o produto.</summary>
    public Guid? CategoriaId { get; init; }

    public ProdutoEdicaoInput ToInput() => new(Nome, Descricao, CategoriaId);
}

/// <summary>Payload para criar ou editar os dados comerciais de uma variante.</summary>
public sealed record ProdutoVarianteRequest
{
    /// <summary>Identificação da apresentação da variante.</summary>
    public string? Identificacao { get; init; }

    /// <summary>Código da unidade de venda da variante.</summary>
    public string? UnidadeVenda { get; init; }

    /// <summary>Preço de venda positivo, com precisão monetária BRL.</summary>
    public decimal PrecoVenda { get; init; }

    public ProdutoVarianteInput ToInput() => new(Identificacao, UnidadeVenda, PrecoVenda);
}

/// <summary>Payload para ativar ou desativar uma variante.</summary>
public sealed record ProdutoVarianteStatusRequest
{
    /// <summary>Novo estado ativo da variante.</summary>
    public bool? Ativa { get; init; }

    /// <summary>Variante ativa promovida ao desativar a padrão atual.</summary>
    public Guid? NovoPadraoId { get; init; }
}

/// <summary>Payload para publicar ou despublicar um produto.</summary>
public sealed record ProdutoPublicacaoRequest
{
    /// <summary>Novo estado de publicação.</summary>
    public bool? Publicado { get; init; }
}

/// <summary>Representação administrativa de uma variante de produto.</summary>
public sealed record ProdutoVarianteResponse(
    Guid Id,
    string Identificacao,
    string UnidadeVenda,
    decimal PrecoVenda,
    bool Ativa,
    bool Padrao)
{
    public static ProdutoVarianteResponse From(ProdutoVariante variante) =>
        new(
            variante.Id,
            variante.Identificacao,
            variante.UnidadeVenda,
            variante.PrecoVenda,
            variante.Ativa,
            variante.Padrao);
}

/// <summary>Representação administrativa de um produto e suas variantes.</summary>
public sealed record ProdutoResponse(
    Guid Id,
    string Nome,
    string? Descricao,
    Guid CategoriaId,
    string CategoriaNome,
    bool CategoriaAtiva,
    bool Publicado,
    IReadOnlyList<ProdutoVarianteResponse> Variantes)
{
    public static ProdutoResponse From(Produto produto) =>
        new(
            produto.Id,
            produto.Nome,
            produto.Descricao,
            produto.CategoriaId,
            produto.Categoria.Nome,
            produto.Categoria.Ativa,
            produto.Publicado,
            produto.Variantes
                .OrderByDescending(variante => variante.Padrao)
                .ThenBy(variante => variante.IdentificacaoNormalizada, StringComparer.Ordinal)
                .Select(ProdutoVarianteResponse.From)
                .ToArray());
}
