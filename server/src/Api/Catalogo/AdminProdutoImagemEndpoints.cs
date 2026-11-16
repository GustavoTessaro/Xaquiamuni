using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Catalogo.Imagens;

namespace Xaquiamuni.Api.Catalogo;

public static class AdminProdutoImagemEndpoints
{
    public const long TamanhoMaximoArquivoBytes = 5 * 1024 * 1024;
    public const long TamanhoMaximoRequisicaoBytes = TamanhoMaximoArquivoBytes + 64 * 1024;
    public const string CampoArquivo = "arquivo";
    public const string CampoVarianteId = "varianteIds";

    private const int QuantidadeMaximaValoresFormulario = 256;
    private const int TamanhoMaximoValorFormulario = 64;

    public static IEndpointRouteBuilder MapAdminProdutoImagemEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"{AdminProdutoEndpoints.BasePath}/{{produtoId:guid}}/imagens")
            .RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithTags("Imagens administrativas de produtos")
            .WithDescription("Acesso restrito a sessões autenticadas com o papel ADMIN.");

        group.MapGet("", ListarAsync)
            .WithName("ListAdminProductImages")
            .WithSummary("Lista as imagens ordenadas de um produto.")
            .WithDescription("Retorna somente metadados seguros e a rota relativa para o conteúdo.")
            .Produces<IReadOnlyList<ProdutoImagemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{imagemId:guid}", ConsultarAsync)
            .WithName("GetAdminProductImage")
            .WithSummary("Consulta os metadados de uma imagem do produto.")
            .Produces<ProdutoImagemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{imagemId:guid}/conteudo", AbrirConteudoAsync)
            .WithName("GetAdminProductImageContent")
            .WithSummary("Transmite o conteúdo WebP de uma imagem administrativa.")
            .WithDescription("A resposta não é armazenada em cache e não expõe a chave de armazenamento.")
            .Produces(StatusCodes.Status200OK, contentType: "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("", AdicionarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(TamanhoMaximoRequisicaoBytes),
                new RequestFormLimitsAttribute
                {
                    MultipartBodyLengthLimit = TamanhoMaximoRequisicaoBytes,
                    ValueCountLimit = QuantidadeMaximaValoresFormulario,
                    ValueLengthLimit = TamanhoMaximoValorFormulario,
                    KeyLengthLimit = 32,
                    MultipartBoundaryLengthLimit = 128,
                    MultipartHeadersCountLimit = 16,
                    MultipartHeadersLengthLimit = 16 * 1024
                })
            .Accepts<AdicionarProdutoImagemRequest>("multipart/form-data")
            .WithName("CreateAdminProductImage")
            .WithSummary("Adiciona uma imagem processada à galeria do produto.")
            .WithDescription(
                "Aceita exatamente um arquivo no campo 'arquivo' e IDs opcionais repetidos no campo 'varianteIds'. "
                + "O arquivo é limitado a 5 MiB e a requisição a 5 MiB mais 64 KiB.")
            .Produces<ProdutoImagemResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{imagemId:guid}/principal", DefinirPrincipalAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminProductPrimaryImage")
            .WithSummary("Define a imagem principal do produto.")
            .Produces<IReadOnlyList<ProdutoImagemResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/ordem", ReordenarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("ReorderAdminProductImages")
            .WithSummary("Reordena todas as imagens do produto.")
            .WithDescription("A lista deve conter todos os IDs existentes, sem duplicidades.")
            .Produces<IReadOnlyList<ProdutoImagemResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{imagemId:guid}/variantes/{varianteId:guid}", AssociarVarianteAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("AssociateAdminProductImageVariant")
            .WithSummary("Vincula uma imagem a uma variante do mesmo produto.")
            .Produces<IReadOnlyList<ProdutoImagemResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{imagemId:guid}/variantes/{varianteId:guid}", DesassociarVarianteAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("UnassociateAdminProductImageVariant")
            .WithSummary("Remove o vínculo entre uma imagem e uma variante.")
            .Produces<IReadOnlyList<ProdutoImagemResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{imagemId:guid}", RemoverAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("DeleteAdminProductImage")
            .WithSummary("Remove uma imagem do produto.")
            .WithDescription(
                "A remoção lógica é concluída antes da exclusão física. Uma limpeza pendente é informada explicitamente.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProdutoImagemRemocaoResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> ListarAsync(
        Guid produtoId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.ListarAsync(produtoId, cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? Results.Ok(result.Imagens.Select(ProdutoImagemResponse.From).ToArray())
            : MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> ConsultarAsync(
        Guid produtoId,
        Guid imagemId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.ConsultarAsync(produtoId, imagemId, cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? Results.Ok(ProdutoImagemResponse.From(result.Imagem!))
            : MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> AbrirConteudoAsync(
        Guid produtoId,
        Guid imagemId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.AbrirConteudoAsync(produtoId, imagemId, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != ProdutoImagemOperacaoStatus.Success)
        {
            result.Dispose();
            return MapFailure(result.Status, [], result.Mensagem);
        }

        return new ProdutoImagemConteudoHttpResult(result);
    }

    private static async Task<IResult> AdicionarAsync(
        HttpContext context,
        Guid produtoId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType
            || !string.Equals(
                context.Request.ContentType?.Split(';', 2)[0].Trim(),
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Tipo de mídia não suportado",
                "O upload deve usar multipart/form-data.");
        }

        if (context.Request.ContentLength is > TamanhoMaximoRequisicaoBytes)
        {
            return PayloadTooLarge();
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            if (exception.Message.Contains("body length limit", StringComparison.OrdinalIgnoreCase))
            {
                return PayloadTooLarge();
            }

            return ValidationProblem(
                "formulario",
                "O formulário multipart é inválido ou excede os limites permitidos.");
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return PayloadTooLarge();
        }

        if (form.Keys.Any(key => !string.Equals(key, CampoVarianteId, StringComparison.Ordinal)))
        {
            return ValidationProblem("formulario", "O formulário contém campos não suportados.");
        }

        if (form.Files.Count != 1
            || !string.Equals(form.Files[0].Name, CampoArquivo, StringComparison.Ordinal))
        {
            return ValidationProblem("arquivo", "Envie exatamente um arquivo no campo 'arquivo'.");
        }

        var arquivo = form.Files[0];
        if (arquivo.Length == 0)
        {
            return ValidationProblem("arquivo", "O arquivo não pode estar vazio.");
        }

        if (arquivo.Length > TamanhoMaximoArquivoBytes)
        {
            return PayloadTooLarge();
        }

        var varianteIds = new Guid[form[CampoVarianteId].Count];
        for (var index = 0; index < varianteIds.Length; index++)
        {
            if (!Guid.TryParse(form[CampoVarianteId][index], out varianteIds[index]))
            {
                return ValidationProblem(
                    CampoVarianteId,
                    "Cada valor de 'varianteIds' deve conter um identificador GUID válido.");
            }
        }

        using var stream = arquivo.OpenReadStream();
        var result = await gerenciarImagens.AdicionarAsync(
                produtoId,
                new AdicionarImagemProdutoInput(stream, arquivo.FileName, varianteIds),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == ProdutoImagemOperacaoStatus.Success)
        {
            var imagem = ProdutoImagemResponse.From(result.Imagem!);
            return Results.Created(ImagemPath(produtoId, imagem.Id), imagem);
        }

        return MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> DefinirPrincipalAsync(
        Guid produtoId,
        Guid imagemId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.DefinirPrincipalAsync(produtoId, imagemId, cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? Results.Ok(result.Imagens.Select(ProdutoImagemResponse.From).ToArray())
            : MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> ReordenarAsync(
        Guid produtoId,
        ReordenarProdutoImagensRequest? request,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        if (request?.ImagemIds is null)
        {
            return ValidationProblem("imagemIds", "Informe a lista completa dos IDs na ordem desejada.");
        }

        var result = await gerenciarImagens.ReordenarAsync(produtoId, request.ImagemIds, cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? Results.Ok(result.Imagens.Select(ProdutoImagemResponse.From).ToArray())
            : MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> AssociarVarianteAsync(
        Guid produtoId,
        Guid imagemId,
        Guid varianteId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.AssociarVarianteAsync(
                produtoId,
                imagemId,
                varianteId,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? Results.Ok(result.Imagens.Select(ProdutoImagemResponse.From).ToArray())
            : MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> DesassociarVarianteAsync(
        Guid produtoId,
        Guid imagemId,
        Guid varianteId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.DesassociarVarianteAsync(
                produtoId,
                imagemId,
                varianteId,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? Results.Ok(result.Imagens.Select(ProdutoImagemResponse.From).ToArray())
            : MapFailure(result.Status, result.Erros, result.Mensagem);
    }

    private static async Task<IResult> RemoverAsync(
        Guid produtoId,
        Guid imagemId,
        IGerenciarImagensProduto gerenciarImagens,
        CancellationToken cancellationToken)
    {
        var result = await gerenciarImagens.RemoverAsync(produtoId, imagemId, cancellationToken)
            .ConfigureAwait(false);
        return result.Status switch
        {
            ProdutoImagemOperacaoStatus.Success => Results.NoContent(),
            ProdutoImagemOperacaoStatus.CleanupPending => Results.Ok(
                new ProdutoImagemRemocaoResponse(
                    true,
                    false,
                    "A imagem foi removida do catálogo; a exclusão física está pendente.")),
            _ => MapFailure(result.Status, result.Erros, result.Mensagem)
        };
    }

    private static IResult MapFailure(
        ProdutoImagemOperacaoStatus status,
        IReadOnlyList<ProdutoValidationError> errors,
        string? message) =>
        status switch
        {
            ProdutoImagemOperacaoStatus.ValidationFailed => Results.ValidationProblem(
                errors
                    .GroupBy(error => error.Campo, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Mensagem).ToArray(),
                        StringComparer.Ordinal)),
            ProdutoImagemOperacaoStatus.ProductNotFound => NotFound("Produto não encontrado."),
            ProdutoImagemOperacaoStatus.ImageNotFound => NotFound("Imagem não encontrada neste produto."),
            ProdutoImagemOperacaoStatus.VariantNotFound => NotFound("Variante não encontrada neste produto."),
            ProdutoImagemOperacaoStatus.Conflict => Problem(
                StatusCodes.Status409Conflict,
                "Conflito na galeria",
                message),
            ProdutoImagemOperacaoStatus.InvalidImage => ValidationProblem(
                "arquivo",
                message ?? "O conteúdo enviado não é uma imagem válida."),
            ProdutoImagemOperacaoStatus.StoredFileMissing => NotFound(
                "Os metadados da imagem existem, mas o arquivo físico não está disponível."),
            ProdutoImagemOperacaoStatus.StorageUnavailable => Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Armazenamento indisponível",
                "Não foi possível acessar o armazenamento da imagem."),
            ProdutoImagemOperacaoStatus.CommitOutcomeUnknown => Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Resultado da operação desconhecido",
                "Não repita o upload automaticamente. Consulte a galeria antes de iniciar outra operação."),
            ProdutoImagemOperacaoStatus.CompensationPending => Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Limpeza pendente",
                "A persistência não foi confirmada e a limpeza do arquivo está pendente."),
            ProdutoImagemOperacaoStatus.CleanupPending => Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Limpeza pendente",
                "A operação não confirmou a remoção lógica; consulte a galeria antes de tentar novamente."),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported image operation status.")
        };

    private static IResult PayloadTooLarge() =>
        Problem(
            StatusCodes.Status413PayloadTooLarge,
            "Upload excede o limite",
            "O arquivo pode ter no máximo 5 MiB e a requisição multipart, no máximo 5 MiB mais 64 KiB.");

    private static IResult ValidationProblem(string field, string detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [detail]
        });

    private static IResult NotFound(string detail) =>
        Problem(StatusCodes.Status404NotFound, "Recurso não encontrado", detail);

    private static IResult Problem(int status, string title, string? detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail);

    private static string ImagemPath(Guid produtoId, Guid imagemId) =>
        $"{AdminProdutoEndpoints.BasePath}/{produtoId.ToString("D", CultureInfo.InvariantCulture)}"
        + $"/imagens/{imagemId.ToString("D", CultureInfo.InvariantCulture)}";

    private sealed class ProdutoImagemConteudoHttpResult(ProdutoImagemConteudoResult result) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            using (result)
            {
                var response = httpContext.Response;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "image/webp";
                response.ContentLength = result.Imagem!.TamanhoBytes;
                response.Headers.CacheControl = "private, no-store";
                response.Headers.XContentTypeOptions = "nosniff";
                response.Headers.ContentDisposition = "inline";

                await result.Conteudo!.CopyToAsync(
                        response.Body,
                        81920,
                        httpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Multipart de inclusão: campo 'arquivo' único e valores repetidos 'varianteIds' opcionais.</summary>
public sealed record AdicionarProdutoImagemRequest
{
    /// <summary>Arquivo de imagem. O endpoint aceita exatamente um arquivo de até 5 MiB.</summary>
    public IFormFile? Arquivo { get; init; }

    /// <summary>IDs opcionais das variantes, enviados como campos 'varianteIds' repetidos.</summary>
    public IReadOnlyList<string>? VarianteIds { get; init; }
}

/// <summary>IDs das imagens do produto na ordem completa desejada.</summary>
public sealed record ReordenarProdutoImagensRequest
{
    /// <summary>Lista completa, sem repetição, dos IDs das imagens do produto.</summary>
    public IReadOnlyList<Guid>? ImagemIds { get; init; }
}

/// <summary>Metadados de imagem administrativos sem caminho ou chave interna de armazenamento.</summary>
public sealed record ProdutoImagemResponse(
    Guid Id,
    Guid ProdutoId,
    string NomeOriginal,
    string TipoConteudo,
    long TamanhoBytes,
    int LarguraPixels,
    int AlturaPixels,
    int OrdemExibicao,
    bool Principal,
    IReadOnlyList<Guid> VarianteIds,
    DateTimeOffset CriadoEmUtc,
    string ConteudoUrl)
{
    public static ProdutoImagemResponse From(ProdutoImagemDto imagem) =>
        new(
            imagem.Id,
            imagem.ProdutoId,
            imagem.NomeOriginal,
            imagem.TipoConteudo,
            imagem.TamanhoBytes,
            imagem.LarguraPixels,
            imagem.AlturaPixels,
            imagem.OrdemExibicao,
            imagem.Principal,
            imagem.VarianteIds,
            imagem.CriadoEmUtc,
            CriarUrlConteudo(imagem.ProdutoId, imagem.Id));

    public static string CriarUrlConteudo(Guid produtoId, Guid imagemId) =>
        $"{AdminProdutoEndpoints.BasePath}/{produtoId:D}/imagens/{imagemId:D}/conteudo";
}

/// <summary>Confirma a remoção lógica da imagem e informa eventual limpeza física pendente.</summary>
public sealed record ProdutoImagemRemocaoResponse(
    bool RemovidaDoCatalogo,
    bool LimpezaFisicaConcluida,
    string Mensagem);
