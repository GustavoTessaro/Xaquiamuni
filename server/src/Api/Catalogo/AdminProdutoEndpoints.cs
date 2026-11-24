using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
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
            .WithSummary("Consulta um produto, suas características, valores e variantes.")
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
            .WithSummary("Edita dados, características, valores e variantes de um produto.")
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
            .WithSummary("Edita dados comerciais, medidas e combinação da variante.")
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

        group.MapDelete("/{id:guid}/variantes/{varianteId:guid}", ExcluirVarianteAsync)
            .RequireXaquiamuniAntiforgery();
        group.MapDelete("/{id:guid}", ExcluirProdutoAsync)
            .RequireXaquiamuniAntiforgery();
        return endpoints;
    }

    private static Task<IResult> ExcluirVarianteAsync(Guid id, Guid varianteId, HttpContext contexto,
        IExcluirVariante exclusao, CancellationToken ct) =>
        ExecutarExclusaoAsync(contexto, id != Guid.Empty && varianteId != Guid.Empty, "variante_invalida",
            (versao, autor, chave) => exclusao.ExecutarAsync(id, varianteId, versao, autor, chave, CalcularVersao, ct));

    private static Task<IResult> ExcluirProdutoAsync(Guid id, HttpContext contexto,
        IExcluirProduto exclusao, CancellationToken ct) =>
        ExecutarExclusaoAsync(contexto, id != Guid.Empty, "produto_invalido",
            (versao, autor, chave) => exclusao.ExecutarAsync(id, versao, autor, chave, CalcularVersao, ct));

    private static string CalcularVersao(Produto produto) => ProdutoResponse.From(produto).Versao;

    private static async Task<IResult> ExecutarExclusaoAsync(HttpContext contexto, bool identificadoresValidos,
        string codigoIdentificador, Func<string, Guid, Guid, Task<(int Status, string? Codigo)>> executar)
    {
        contexto.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var autor) || autor == Guid.Empty)
            return Results.Forbid();
        var tags = contexto.Request.Headers.IfMatch;
        if (!identificadoresValidos)
            return ProblemaExclusao(400, codigoIdentificador);
        if (tags.Count != 1 || tags[0] is not { Length: 66 } tag || tag[0] != '"' || tag[^1] != '"' ||
            !tag[1..^1].All(Uri.IsHexDigit))
            return ProblemaExclusao(400, "versao_invalida");
        var chaves = contexto.Request.Headers["Idempotency-Key"];
        if (chaves.Count != 1 || !Guid.TryParseExact(chaves[0], "D", out var chave) || chave == Guid.Empty)
            return ProblemaExclusao(400, "chave_invalida");
        var (status, codigo) = await executar(tag[1..^1].ToUpperInvariant(), autor, chave);
        return status == 204 ? Results.NoContent() : ProblemaExclusao(status, codigo!);
    }

    private static IResult ProblemaExclusao(int status, string codigo) => Results.Problem(statusCode: status, detail: codigo switch
    {
        "ultima_variante" => "O produto precisa manter pelo menos uma variante.",
        "variante_padrao" => "Defina outra variante padrão antes de excluir esta.",
        "variante_com_historico_comercial" => "A variante possui vínculos comerciais e precisa ser preservada.",
        "variante_com_historico_estoque" => "A variante possui registros de estoque e precisa ser preservada.",
        "variante_com_historico_producao" => "A variante possui registros de produção e precisa ser preservada.",
        "variante_historico_inconclusivo" => "Não foi possível verificar o histórico com segurança.",
        "variante_nao_encontrada" => "Variante não encontrada neste produto.",
        "produto_nao_encontrado" => "Produto não encontrado.",
        "produto_invalido" => "Informe um produto válido.",
        "produto_com_historico_comercial" => "O produto possui vínculos comerciais e precisa ser preservado.",
        "produto_historico_inconclusivo" => "Não foi possível verificar o histórico do produto com segurança.",
        "produto_imagem_inconclusiva" => "Não foi possível verificar o armazenamento de uma imagem com segurança.",
        "versao_invalida" => "Informe a versão atual no cabeçalho If-Match.",
        "variante_invalida" => "Informe produto e variante válidos.",
        "chave_invalida" => "Informe uma chave de idempotência UUID.",
        "chave_em_uso" => "A chave já foi usada para outra operação.",
        _ => "O catálogo foi alterado. Recarregue o produto antes de confirmar novamente."
    }, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

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
        HttpContext contexto,
        IGerenciarProdutos gerenciarProdutos,
        CancellationToken cancellationToken)
    {
        var produto = await gerenciarProdutos.ConsultarAsync(id, cancellationToken).ConfigureAwait(false);
        if (produto is null) return NotFound("Produto não encontrado.");
        var resposta = ProdutoResponse.From(produto);
        contexto.Response.Headers.ETag = $"\"{resposta.Versao}\"";
        contexto.Response.Headers.CacheControl = "private, no-store";
        return Results.Ok(resposta);
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

/// <summary>Payload para cadastrar um produto e suas variantes, características e valores.</summary>
public sealed record ProdutoCadastroRequest
{
    /// <summary>Nome do produto.</summary>
    public string? Nome { get; init; }

    /// <summary>Descrição opcional do produto.</summary>
    public string? Descricao { get; init; }

    /// <summary>Identificador da categoria ativa.</summary>
    public Guid? CategoriaId { get; init; }

    /// <summary>Dados comerciais e medidas da variante padrão.</summary>
    public ProdutoVarianteRequest? VariantePadrao { get; init; }

    /// <summary>Variantes adicionais explícitas.</summary>
    public IReadOnlyList<ProdutoVarianteRequest?>? Variantes { get; init; }

    /// <summary>Indica produto simples; sua variante padrão recebe internamente a identificação Padrão.</summary>
    public bool? Simples { get; init; }

    /// <summary>Características e valores pertencentes ao produto.</summary>
    public IReadOnlyList<ProdutoCaracteristicaRequest?>? Caracteristicas { get; init; }

    public ProdutoCadastroInput ToInput() =>
        new(
            Nome,
            Descricao,
            CategoriaId,
            VariantePadrao?.ToInput(),
            Variantes?.Select(item => item?.ToInput()).ToArray(),
            Simples,
            Caracteristicas?.Select(item => item?.ToInput()).ToArray());
}

/// <summary>Payload para editar os dados do produto e, opcionalmente, seu agregado de variantes.</summary>
public sealed record ProdutoEdicaoRequest
{
    /// <summary>Nome do produto.</summary>
    public string? Nome { get; init; }

    /// <summary>Descrição opcional do produto.</summary>
    public string? Descricao { get; init; }

    /// <summary>Categoria desejada para o produto.</summary>
    public Guid? CategoriaId { get; init; }

    /// <summary>Características a adicionar ou atualizar; omissão preserva as existentes.</summary>
    public IReadOnlyList<ProdutoCaracteristicaRequest?>? Caracteristicas { get; init; }

    /// <summary>Variantes a adicionar ou atualizar por ID; omissão preserva todas as variantes.</summary>
    public IReadOnlyList<ProdutoVarianteRequest?>? Variantes { get; init; }

    public ProdutoEdicaoInput ToInput() =>
        new(
            Nome,
            Descricao,
            CategoriaId,
            Caracteristicas?.Select(item => item?.ToInput()).ToArray(),
            Variantes?.Select(item => item?.ToInput()).ToArray());
}

/// <summary>Payload para criar ou editar os dados comerciais, medidas e combinação de uma variante.</summary>
public sealed record ProdutoVarianteRequest
{
    /// <summary>ID persistido; omitido somente para criar uma nova variante.</summary>
    public Guid? Id { get; init; }

    /// <summary>Identificação da apresentação da variante.</summary>
    public string? Identificacao { get; init; }

    /// <summary>Código da unidade de venda da variante.</summary>
    public string? UnidadeVenda { get; init; }

    /// <summary>Preço de venda positivo, com precisão monetária.</summary>
    public decimal? PrecoVenda { get; init; }

    /// <summary>Peso líquido em gramas, opcional.</summary>
    public decimal? PesoLiquidoGramas { get; init; }

    /// <summary>Volume líquido em mililitros, opcional.</summary>
    public decimal? VolumeLiquidoMililitros { get; init; }

    /// <summary>Valores de características selecionados pela variante; IDs ou referências temporárias.</summary>
    public IReadOnlyList<ProdutoVarianteCaracteristicaRequest?>? Caracteristicas { get; init; }

    /// <summary>Estado ativo desejado em uma edição.</summary>
    public bool? Ativa { get; init; }

    /// <summary>Define esta variante como padrão na edição do agregado.</summary>
    public bool? Padrao { get; init; }

    /// <summary>Novo padrão ao desativar a variante padrão atual.</summary>
    public Guid? NovoPadraoId { get; init; }

    /// <summary>Permite limpar explicitamente medidas diretas enviando-as como nulas.</summary>
    public bool AtualizarMedidas { get; init; }

    public ProdutoVarianteInput ToInput() =>
        new(
            Identificacao,
            UnidadeVenda,
            PrecoVenda,
            Id,
            PesoLiquidoGramas,
            VolumeLiquidoMililitros,
            Caracteristicas?.Select(item => item?.ToInput()).ToArray(),
            Ativa,
            Padrao,
            NovoPadraoId,
            AtualizarMedidas);
}

/// <summary>Payload para adicionar ou atualizar uma característica do produto.</summary>
public sealed record ProdutoCaracteristicaRequest
{
    /// <summary>Referência temporária para vínculos no mesmo cadastro.</summary>
    public string? Referencia { get; init; }

    /// <summary>ID persistido para atualizar ou remover uma característica existente.</summary>
    public Guid? Id { get; init; }

    /// <summary>Nome da característica, como cor, fragrância ou tamanho.</summary>
    public string? Nome { get; init; }

    /// <summary>Tipo físico PesoLiquido ou VolumeLiquido; nulo para característica genérica.</summary>
    public string? TipoMedidaFisica { get; init; }

    /// <summary>Valores disponíveis para esta característica.</summary>
    public IReadOnlyList<ProdutoCaracteristicaValorRequest?>? Valores { get; init; }

    /// <summary>Solicita remoção; só é aceita quando não há vínculos em variantes.</summary>
    public bool Remover { get; init; }

    public ProdutoCaracteristicaInput ToInput() =>
        new(
            Referencia,
            Id,
            Nome,
            TipoMedidaFisica,
            Valores?.Select(item => item?.ToInput()).ToArray(),
            Remover);
}

/// <summary>Payload para adicionar, atualizar ou remover um valor de característica.</summary>
public sealed record ProdutoCaracteristicaValorRequest
{
    /// <summary>Referência temporária para vínculos no mesmo cadastro.</summary>
    public string? Referencia { get; init; }

    /// <summary>ID persistido para atualizar ou remover um valor existente.</summary>
    public Guid? Id { get; init; }

    /// <summary>Texto do valor genérico; nulo para valores de medidas físicas.</summary>
    public string? Nome { get; init; }

    /// <summary>Valor numérico em gramas ou mililitros, conforme o tipo da característica.</summary>
    public decimal? ValorMedidaCanonico { get; init; }

    /// <summary>Solicita remoção; só é aceita quando não há vínculos em variantes.</summary>
    public bool Remover { get; init; }

    public ProdutoCaracteristicaValorInput ToInput() =>
        new(Referencia, Id, Nome, ValorMedidaCanonico, Remover);
}

/// <summary>Vínculo de uma variante a um valor, por IDs persistidos ou referências temporárias.</summary>
public sealed record ProdutoVarianteCaracteristicaRequest
{
    /// <summary>ID persistido da característica.</summary>
    public Guid? CaracteristicaId { get; init; }

    /// <summary>Referência da característica enviada nesta mesma operação.</summary>
    public string? ReferenciaCaracteristica { get; init; }

    /// <summary>ID persistido do valor.</summary>
    public Guid? ValorId { get; init; }

    /// <summary>Referência do valor enviado nesta mesma operação.</summary>
    public string? ReferenciaValor { get; init; }

    public ProdutoVarianteCaracteristicaInput ToInput() =>
        new(CaracteristicaId, ReferenciaCaracteristica, ValorId, ReferenciaValor);
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

/// <summary>Associação devolvida para reconstruir as características de uma variante.</summary>
public sealed record ProdutoVarianteCaracteristicaResponse(
    Guid CaracteristicaId,
    string CaracteristicaNome,
    Guid ValorId,
    string ValorNome,
    decimal? ValorMedidaCanonico);

/// <summary>Valor configurado em uma característica do produto.</summary>
public sealed record ProdutoCaracteristicaValorResponse(
    Guid Id,
    string Nome,
    decimal? ValorMedidaCanonico);

/// <summary>Característica configurada no produto e seus valores possíveis.</summary>
public sealed record ProdutoCaracteristicaResponse(
    Guid Id,
    string Nome,
    string? TipoMedidaFisica,
    IReadOnlyList<ProdutoCaracteristicaValorResponse> Valores)
{
    public static ProdutoCaracteristicaResponse From(ProdutoCaracteristica characteristic) =>
        new(
            characteristic.Id,
            characteristic.Nome,
            characteristic.TipoMedidaFisica?.ToString(),
            characteristic.Valores
                .OrderBy(value => value.NomeNormalizado, StringComparer.Ordinal)
                .Select(value => new ProdutoCaracteristicaValorResponse(
                    value.Id,
                    value.Nome,
                    value.ValorMedidaCanonico))
                .ToArray());
}

/// <summary>Representação administrativa completa de uma variante de produto.</summary>
public sealed record ProdutoVarianteResponse(
    Guid Id,
    string Identificacao,
    string UnidadeVenda,
    decimal PrecoVenda,
    bool Ativa,
    bool Padrao,
    decimal? PesoLiquidoGramas,
    decimal? VolumeLiquidoMililitros,
    IReadOnlyList<ProdutoVarianteCaracteristicaResponse> Caracteristicas)
{
    public static ProdutoVarianteResponse From(ProdutoVariante variant) =>
        new(
            variant.Id,
            variant.Identificacao,
            variant.UnidadeVenda,
            variant.PrecoVenda,
            variant.Ativa,
            variant.Padrao,
            variant.PesoLiquidoGramas,
            variant.VolumeLiquidoMililitros,
            variant.CaracteristicaValores
                .OrderBy(value => value.Caracteristica.NomeNormalizado, StringComparer.Ordinal)
                .Select(value => new ProdutoVarianteCaracteristicaResponse(
                    value.Caracteristica.Id,
                    value.Caracteristica.Nome,
                    value.Valor.Id,
                    value.Valor.Nome,
                    value.Valor.ValorMedidaCanonico))
                .ToArray());
}

/// <summary>Representação administrativa completa de um produto e de seu agregado.</summary>
public sealed record ProdutoResponse(
    Guid Id,
    string Nome,
    string? Descricao,
    Guid CategoriaId,
    string CategoriaNome,
    bool CategoriaAtiva,
    bool Publicado,
    int QuantidadeImagens,
    string? FotoPrincipalUrl,
    IReadOnlyList<ProdutoVarianteResponse> Variantes,
    IReadOnlyList<ProdutoCaracteristicaResponse> Caracteristicas)
{
    public string Versao { get; init; } = "";

    public static ProdutoResponse From(Produto produto)
    {
        var imagemPrincipal = produto.Imagens.FirstOrDefault(imagem => imagem.Principal);
        var resposta = new ProdutoResponse(
            produto.Id,
            produto.Nome,
            produto.Descricao,
            produto.CategoriaId,
            produto.Categoria.Nome,
            produto.Categoria.Ativa,
            produto.Publicado,
            produto.Imagens.Count,
            imagemPrincipal is null
                ? null
                : ProdutoImagemResponse.CriarUrlConteudo(produto.Id, imagemPrincipal.Id),
            produto.Variantes
                .OrderByDescending(variant => variant.Padrao)
                .ThenBy(variant => variant.IdentificacaoNormalizada, StringComparer.Ordinal)
                .Select(ProdutoVarianteResponse.From)
                .ToArray(),
            produto.Caracteristicas
                .OrderBy(characteristic => characteristic.NomeNormalizado, StringComparer.Ordinal)
                .Select(ProdutoCaracteristicaResponse.From)
                .ToArray());
        var imagens = produto.Imagens.OrderBy(i => i.Id).Select(i => new
        {
            i.Id, i.Principal, i.OrdemExibicao,
            Variantes = i.Variantes.Select(a => a.ProdutoVarianteId).Order()
        });
        return resposta with
        {
            Versao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { resposta, imagens })))
        };
    }
}
