using Xaquiamuni.Aplicacao.Catalogo.Imagens;
using Xaquiamuni.Aplicacao.Catalogo.Publico;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Catalogo;

public static class CatalogoPublicoEndpoints
{
    public const string BasePath = "/api/catalogo";
    public const int TamanhoPaginaPadrao = 20;
    public const int TamanhoPaginaMaximo = 50;
    private const int TamanhoMaximoBusca = Produto.NomeMaxLength;

    internal static string CriarUrlConteudoImagem(Guid produtoId, Guid imagemId) =>
        $"{BasePath}/produtos/{produtoId:D}/imagens/{imagemId:D}/conteudo";

    public static IEndpointRouteBuilder MapCatalogoPublicoEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .AllowAnonymous()
            .WithTags("Catálogo público");

        group.MapGet("/categorias", ListarCategoriasAsync)
            .WithName("ListPublicCatalogCategories")
            .WithSummary("Lista as categorias ativas do catálogo.")
            .WithDescription("Retorna somente categorias ativas, ordenadas para exibição.")
            .Produces<IReadOnlyList<CategoriaCatalogoPublicoResponse>>(StatusCodes.Status200OK);

        group.MapGet("/produtos", ListarProdutosAsync)
            .WithName("ListPublicCatalogProducts")
            .WithSummary("Lista produtos publicados do catálogo.")
            .WithDescription(
                "Filtra por nome e categoria e pagina resultados. Não informa disponibilidade de estoque.")
            .Produces<CatalogoPublicoPaginaResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/produtos/{produtoId:guid}", ConsultarProdutoAsync)
            .WithName("GetPublicCatalogProduct")
            .WithSummary("Consulta os detalhes públicos de um produto publicado.")
            .WithDescription(
                "Inclui somente opções ativas e suas combinações cadastradas, além da galeria ordenada.")
            .Produces<ProdutoCatalogoDetalheResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet(
                "/produtos/{produtoId:guid}/imagens/{imagemId:guid}/conteudo",
                AbrirConteudoImagemAsync)
            .WithName("GetPublicCatalogProductImageContent")
            .WithSummary("Transmite uma imagem do catálogo público.")
            .WithDescription(
                "Retorna 404 para imagens pertencentes a produtos não publicados ou de categorias inativas.")
            .Produces(StatusCodes.Status200OK, contentType: "image/webp")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> ListarCategoriasAsync(
        IConsultarCatalogoPublico catalogo,
        CancellationToken cancellationToken)
    {
        var categorias = await catalogo.ListarCategoriasAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(categorias.Select(CategoriaCatalogoPublicoResponse.From).ToArray());
    }

    private static async Task<IResult> ListarProdutosAsync(
        string? busca,
        Guid? categoriaId,
        int? pagina,
        int? itensPorPagina,
        IConsultarCatalogoPublico catalogo,
        CancellationToken cancellationToken)
    {
        var paginaSolicitada = pagina ?? 1;
        var tamanhoPagina = itensPorPagina ?? TamanhoPaginaPadrao;
        var termo = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
        if (paginaSolicitada < 1
            || tamanhoPagina is < 1 or > TamanhoPaginaMaximo
            || (long)(paginaSolicitada - 1) * tamanhoPagina > int.MaxValue
            || categoriaId == Guid.Empty
            || termo?.Length > TamanhoMaximoBusca)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["consulta"] =
                [
                    $"A página deve ser positiva, itensPorPagina deve estar entre 1 e {TamanhoPaginaMaximo}, "
                + $"a busca deve ter até {TamanhoMaximoBusca} caracteres e categoriaId não pode ser vazio."
                ]
            });
        }

        var filtro = new CatalogoPublicoFiltro(
            termo,
            categoriaId,
            (paginaSolicitada - 1) * tamanhoPagina,
            tamanhoPagina);
        var resultado = await catalogo.ListarProdutosAsync(filtro, cancellationToken)
            .ConfigureAwait(false);

        var itens = resultado.Produtos.Select(ProdutoCatalogoResumoResponse.From).ToArray();
        var totalPaginas = resultado.TotalItens == 0
            ? 0
            : (int)Math.Ceiling((double)resultado.TotalItens / tamanhoPagina);
        return Results.Ok(new CatalogoPublicoPaginaResponse(
            itens,
            paginaSolicitada,
            tamanhoPagina,
            resultado.TotalItens,
            totalPaginas));
    }

    private static async Task<IResult> ConsultarProdutoAsync(
        Guid produtoId,
        IConsultarCatalogoPublico catalogo,
        CancellationToken cancellationToken)
    {
        var produto = await catalogo.ConsultarProdutoAsync(produtoId, cancellationToken)
            .ConfigureAwait(false);
        return produto is null
            ? Results.NotFound()
            : Results.Ok(ProdutoCatalogoDetalheResponse.From(produto));
    }

    private static async Task<IResult> AbrirConteudoImagemAsync(
        Guid produtoId,
        Guid imagemId,
        IConsultarCatalogoPublico catalogo,
        CancellationToken cancellationToken)
    {
        var result = await catalogo.AbrirConteudoImagemAsync(
                produtoId,
                imagemId,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == ProdutoImagemOperacaoStatus.Success)
        {
            return new ProdutoImagemConteudoHttpResult(result, publicContent: true);
        }

        result.Dispose();
        return result.Status switch
        {
            ProdutoImagemOperacaoStatus.ProductNotFound or ProdutoImagemOperacaoStatus.ImageNotFound =>
                Results.NotFound(),
            ProdutoImagemOperacaoStatus.ValidationFailed =>
                Results.BadRequest(),
            ProdutoImagemOperacaoStatus.StoredFileMissing or ProdutoImagemOperacaoStatus.StorageUnavailable =>
                Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Imagem temporariamente indisponível",
                    detail: "Não foi possível disponibilizar o conteúdo da imagem."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Status,
                "Unsupported public image content result.")
        };
    }
}

/// <summary>Representa uma categoria ativa disponível no catálogo público.</summary>
public sealed record CategoriaCatalogoPublicoResponse(Guid Id, string Nome, string? Descricao)
{
    public static CategoriaCatalogoPublicoResponse From(Categoria categoria) =>
        new(categoria.Id, categoria.Nome, categoria.Descricao);
}

/// <summary>Representa uma unidade e os valores físicos disponíveis nas opções ativas.</summary>
public sealed record ProdutoCatalogoMedidaResponse(
    Guid OpcaoId,
    decimal? PesoLiquidoGramas,
    decimal? VolumeLiquidoMililitros);

/// <summary>Resumo público de um produto publicado para os resultados da listagem.</summary>
public sealed record ProdutoCatalogoResumoResponse(
    Guid Id,
    string Nome,
    Guid CategoriaId,
    string CategoriaNome,
    ProdutoCatalogoFotoPrincipalResponse FotoPrincipal,
    decimal PrecoMinimo,
    decimal PrecoMaximo,
    IReadOnlyList<string> UnidadesVenda,
    IReadOnlyList<ProdutoCatalogoMedidaResponse> Medidas)
{
    public static ProdutoCatalogoResumoResponse From(Produto produto)
    {
        var opcoesAtivas = produto.Variantes.Where(variante => variante.Ativa).ToArray();
        var imagemPrincipal = produto.Imagens.Single(imagem => imagem.Principal);
        return new ProdutoCatalogoResumoResponse(
            produto.Id,
            produto.Nome,
            produto.CategoriaId,
            produto.Categoria.Nome,
            ProdutoCatalogoFotoPrincipalResponse.From(produto.Id, imagemPrincipal),
            opcoesAtivas.Min(opcao => opcao.PrecoVenda),
            opcoesAtivas.Max(opcao => opcao.PrecoVenda),
            opcoesAtivas
                .Select(opcao => opcao.UnidadeVenda)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(unidade => unidade, StringComparer.Ordinal)
                .ToArray(),
            opcoesAtivas
                .Where(opcao => opcao.PesoLiquidoGramas is not null
                    || opcao.VolumeLiquidoMililitros is not null)
                .Select(opcao => new ProdutoCatalogoMedidaResponse(
                    opcao.Id,
                    opcao.PesoLiquidoGramas,
                    opcao.VolumeLiquidoMililitros))
                .ToArray());
    }
}

/// <summary>Uma página ordenada de produtos públicos com metadados de paginação.</summary>
public sealed record CatalogoPublicoPaginaResponse(
    IReadOnlyList<ProdutoCatalogoResumoResponse> Itens,
    int Pagina,
    int ItensPorPagina,
    int TotalItens,
    int TotalPaginas);

/// <summary>Foto principal resumida para a listagem pública de produtos.</summary>
public sealed record ProdutoCatalogoFotoPrincipalResponse(Guid Id, string ConteudoUrl)
{
    public static ProdutoCatalogoFotoPrincipalResponse From(Guid produtoId, ProdutoImagem imagem) =>
        new(imagem.Id, CatalogoPublicoEndpoints.CriarUrlConteudoImagem(produtoId, imagem.Id));
}

/// <summary>Imagem de uma galeria pública, com vínculos somente a opções ativas.</summary>
public sealed record ProdutoCatalogoImagemResponse(
    Guid Id,
    string ConteudoUrl,
    int OrdemExibicao,
    bool Principal,
    IReadOnlyList<Guid> OpcaoIds)
{
    public static ProdutoCatalogoImagemResponse From(Guid produtoId, ProdutoImagem imagem) =>
        new(
            imagem.Id,
            CatalogoPublicoEndpoints.CriarUrlConteudoImagem(produtoId, imagem.Id),
            imagem.OrdemExibicao,
            imagem.Principal,
            imagem.Variantes
                .Select(vinculo => vinculo.ProdutoVarianteId)
                .OrderBy(varianteId => varianteId)
                .ToArray());
}

/// <summary>Valor público selecionado para uma característica em uma opção real.</summary>
public sealed record ProdutoCatalogoValorSelecionadoResponse(
    Guid Id,
    string Nome,
    decimal? ValorMedidaCanonico);

/// <summary>Característica pública com os valores usados pelas opções ativas.</summary>
public sealed record ProdutoCatalogoCaracteristicaResponse(
    Guid Id,
    string Nome,
    string? TipoMedidaFisica,
    IReadOnlyList<ProdutoCatalogoValorSelecionadoResponse> Valores);

/// <summary>Uma opção ativa real do produto, com preço, unidade, medidas e combinação.</summary>
public sealed record ProdutoCatalogoOpcaoResponse(
    Guid Id,
    string? Identificacao,
    string UnidadeVenda,
    decimal PrecoVenda,
    bool Principal,
    decimal? PesoLiquidoGramas,
    decimal? VolumeLiquidoMililitros,
    IReadOnlyList<ProdutoCatalogoCaracteristicaSelecaoResponse> Caracteristicas);

/// <summary>Uma característica e o valor efetivamente associados a uma opção.</summary>
public sealed record ProdutoCatalogoCaracteristicaSelecaoResponse(
    Guid CaracteristicaId,
    string CaracteristicaNome,
    Guid ValorId,
    string ValorNome,
    decimal? ValorMedidaCanonico);

/// <summary>Detalhes públicos de um produto publicado e de suas opções comercializadas.</summary>
public sealed record ProdutoCatalogoDetalheResponse(
    Guid Id,
    string Nome,
    string? Descricao,
    Guid CategoriaId,
    string CategoriaNome,
    bool RequerSelecaoDeOpcao,
    Guid OpcaoPrincipalId,
    ProdutoCatalogoImagemResponse FotoPrincipal,
    IReadOnlyList<ProdutoCatalogoImagemResponse> Galeria,
    IReadOnlyList<ProdutoCatalogoCaracteristicaResponse> Caracteristicas,
    IReadOnlyList<ProdutoCatalogoOpcaoResponse> Opcoes)
{
    public static ProdutoCatalogoDetalheResponse From(Produto produto)
    {
        var opcoesAtivas = produto.Variantes
            .Where(variante => variante.Ativa)
            .OrderByDescending(variante => variante.Padrao)
            .ThenBy(variante => variante.IdentificacaoNormalizada, StringComparer.Ordinal)
            .ToArray();
        var idsValoresAtivos = opcoesAtivas
            .SelectMany(opcao => opcao.CaracteristicaValores)
            .Select(associacao => associacao.ProdutoCaracteristicaValorId)
            .ToHashSet();
        var caracteristicas = produto.Caracteristicas
            .Select(caracteristica => new ProdutoCatalogoCaracteristicaResponse(
                caracteristica.Id,
                caracteristica.Nome,
                caracteristica.TipoMedidaFisica?.ToString(),
                caracteristica.Valores
                    .Where(valor => idsValoresAtivos.Contains(valor.Id))
                    .OrderBy(valor => valor.NomeNormalizado, StringComparer.Ordinal)
                    .Select(valor => new ProdutoCatalogoValorSelecionadoResponse(
                        valor.Id,
                        valor.Nome,
                        valor.ValorMedidaCanonico))
                    .ToArray()))
            .Where(caracteristica => caracteristica.Valores.Count > 0)
            .OrderBy(caracteristica => caracteristica.Nome, StringComparer.Ordinal)
            .ToArray();
        var idsOpcoesAtivas = opcoesAtivas.Select(opcao => opcao.Id).ToHashSet();
        var galeria = produto.Imagens
            .OrderBy(imagem => imagem.OrdemExibicao)
            .Select(imagem => ProdutoCatalogoImagemResponse.From(produto.Id, imagem) with
            {
                OpcaoIds = imagem.Variantes
                    .Where(vinculo => idsOpcoesAtivas.Contains(vinculo.ProdutoVarianteId))
                    .Select(vinculo => vinculo.ProdutoVarianteId)
                    .Order()
                    .ToArray()
            })
            .ToArray();
        var opcaoPrincipal = opcoesAtivas.Single(opcao => opcao.Padrao);
        var isSimpleProduct = produto.Caracteristicas.Count == 0
            && opcoesAtivas.Length == 1
            && string.Equals(
                opcoesAtivas[0].Identificacao,
                Produto.IdentificacaoVariantePadrao,
                StringComparison.Ordinal);
        var opcoes = opcoesAtivas
            .Select(opcao => new ProdutoCatalogoOpcaoResponse(
                opcao.Id,
                isSimpleProduct ? null : opcao.Identificacao,
                opcao.UnidadeVenda,
                opcao.PrecoVenda,
                opcao.Padrao,
                opcao.PesoLiquidoGramas,
                opcao.VolumeLiquidoMililitros,
                opcao.CaracteristicaValores
                    .OrderBy(associacao => associacao.Caracteristica.NomeNormalizado, StringComparer.Ordinal)
                    .Select(associacao => new ProdutoCatalogoCaracteristicaSelecaoResponse(
                        associacao.ProdutoCaracteristicaId,
                        associacao.Caracteristica.Nome,
                        associacao.ProdutoCaracteristicaValorId,
                        associacao.Valor.Nome,
                        associacao.Valor.ValorMedidaCanonico))
                    .ToArray()))
            .ToArray();
        var imagemPrincipal = galeria.Single(imagem => imagem.Principal);

        return new ProdutoCatalogoDetalheResponse(
            produto.Id,
            produto.Nome,
            produto.Descricao,
            produto.CategoriaId,
            produto.Categoria.Nome,
            opcoesAtivas.Length > 1,
            opcaoPrincipal.Id,
            imagemPrincipal,
            galeria,
            caracteristicas,
            opcoes);
    }
}
