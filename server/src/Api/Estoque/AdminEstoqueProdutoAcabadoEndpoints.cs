using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Api.Estoque;

public static class AdminEstoqueProdutoAcabadoEndpoints
{
    public const string BasePath = "/api/admin/estoque/produtos-acabados";

    public static IEndpointRouteBuilder MapAdminEstoqueProdutoAcabadoEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithTags("Estoque administrativo de produtos acabados")
            .WithDescription("Acesso restrito a sessões autenticadas com o papel ADMIN.");

        group.MapGet("/variantes/{varianteId:guid}/politica", ConsultarPoliticaAsync)
            .Produces<PoliticaProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404);

        group.MapPut("/variantes/{varianteId:guid}/politica", ConfigurarPoliticaAsync)
            .RequireXaquiamuniAntiforgery()
            .Produces<PoliticaProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404);

        group.MapGet("/variantes/{varianteId:guid}/posicao", ConsultarPosicaoAsync)
            .Produces<PosicaoProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404);

        group.MapGet("/variantes/{varianteId:guid}/lotes", ConsultarLotesAsync)
            .Produces<IReadOnlyList<LoteProdutoAcabadoAdminResponse>>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404);

        group.MapGet("/variantes/{varianteId:guid}/lotes/{loteId:guid}", ConsultarLoteAsync)
            .Produces<LoteProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404);

        group.MapGet("/variantes/{varianteId:guid}/movimentacoes", ListarMovimentosAsync)
            .Produces<PaginaAdminProdutoAcabadoResponse<MovimentoProdutoAcabadoAdminResponse>>(200)
            .ProducesValidationProblem(400);

        group.MapGet("/variantes/{varianteId:guid}/movimentacoes/{movimentoId:guid}",
                ConsultarMovimentoAsync)
            .Produces<MovimentoProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404);

        group.MapPost("/variantes/{varianteId:guid}/entradas", RegistrarEntradaAsync)
            .RequireXaquiamuniAntiforgery()
            .Produces<MovimentoProdutoAcabadoAdminResponse>(201)
            .Produces<MovimentoProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/variantes/{varianteId:guid}/saidas", RegistrarSaidaAsync)
            .RequireXaquiamuniAntiforgery()
            .Produces<MovimentoProdutoAcabadoAdminResponse>(201)
            .Produces<MovimentoProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/variantes/{varianteId:guid}/transferencias", TransferirAsync)
            .RequireXaquiamuniAntiforgery()
            .Produces<MovimentoProdutoAcabadoAdminResponse>(201)
            .Produces<MovimentoProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/variantes/{varianteId:guid}/contagens", ContarAsync)
            .RequireXaquiamuniAntiforgery()
            .Produces<ContagemProdutoAcabadoAdminResponse>(200)
            .Produces<MovimentoProdutoAcabadoAdminResponse[]>(201)
            .ProducesValidationProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost(
                "/variantes/{varianteId:guid}/movimentacoes/{movimentoId:guid}/estorno",
                EstornarAsync)
            .RequireXaquiamuniAntiforgery()
            .Produces<MovimentoProdutoAcabadoAdminResponse>(201)
            .Produces<MovimentoProdutoAcabadoAdminResponse>(200)
            .ProducesValidationProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        return endpoints;
    }

    private static async Task<IResult> ConsultarPoliticaAsync(
        Guid varianteId,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        var detalhe = await consulta.ConsultarPosicaoVarianteAsync(varianteId, cancellationToken)
            .ConfigureAwait(false);
        return detalhe is null
            ? ResourceNotFound()
            : Results.Ok(detalhe.Politica is null
                ? new PoliticaProdutoAcabadoAdminResponse(varianteId, null, null)
                : PoliticaProdutoAcabadoAdminResponse.From(detalhe.Politica));
    }

    private static async Task<IResult> ConfigurarPoliticaAsync(
        Guid varianteId,
        PoliticaProdutoAcabadoAdminRequest? request,
        IConfigurarPoliticaEstoqueProdutoAcabado configurar,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        if (request is null)
        {
            return ValidationError("Os dados da política são obrigatórios.");
        }

        var result = await configurar.ExecuteAsync(
            new ConfigurarPoliticaProdutoAcabadoInput(
                varianteId,
                request.EstoqueMinimo,
                request.PrazoValidadePadraoDias),
            cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            ConfigurarPoliticaProdutoAcabadoStatus.Sucesso =>
                Results.Ok(new PoliticaProdutoAcabadoAdminResponse(
                    varianteId,
                    result.Politica!.EstoqueMinimo,
                    result.Politica.PrazoValidadePadraoDias)),
            ConfigurarPoliticaProdutoAcabadoStatus.VarianteNaoEncontrada => ResourceNotFound(),
            _ => ValidationError(result.Erro)
        };
    }

    private static async Task<IResult> ConsultarPosicaoAsync(
        Guid varianteId,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        var detalhe = await consulta.ConsultarPosicaoVarianteAsync(varianteId, cancellationToken)
            .ConfigureAwait(false);
        return detalhe is null
            ? ResourceNotFound()
            : Results.Ok(PosicaoProdutoAcabadoAdminResponse.From(detalhe));
    }

    private static async Task<IResult> ConsultarLotesAsync(
        Guid varianteId,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        var detalhe = await consulta.ConsultarPosicaoVarianteAsync(varianteId, cancellationToken)
            .ConfigureAwait(false);
        return detalhe is null
            ? ResourceNotFound()
            : Results.Ok(detalhe.Lotes.Select(LoteProdutoAcabadoAdminResponse.From).ToArray());
    }

    private static async Task<IResult> ConsultarLoteAsync(
        Guid varianteId,
        Guid loteId,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId, loteId) is { } validation)
        {
            return validation;
        }

        var lote = await consulta.ConsultarPosicaoLoteAsync(
                varianteId,
                loteId,
                cancellationToken)
            .ConfigureAwait(false);
        return lote is null
            ? ResourceNotFound()
            : Results.Ok(LoteProdutoAcabadoAdminResponse.From(lote));
    }

    private static async Task<IResult> ListarMovimentosAsync(
        Guid varianteId,
        TipoMovimentoProdutoAcabado? tipo,
        Guid? loteId,
        int? pagina,
        int? itensPorPagina,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId, loteId) is { } validation)
        {
            return validation;
        }

        if (tipo is not null && !Enum.IsDefined(tipo.Value))
        {
            return ValidationError("O tipo de movimento informado é inválido.");
        }

        try
        {
            var result = await consulta.ListarMovimentosAsync(
                new ListarMovimentosProdutoAcabadoFiltro(
                    varianteId,
                    tipo,
                    loteId,
                    pagina ?? 1,
                    itensPorPagina ?? 20),
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(new PaginaAdminProdutoAcabadoResponse<MovimentoProdutoAcabadoAdminResponse>(
                result.Itens.Select(MovimentoProdutoAcabadoAdminResponse.From).ToArray(),
                result.TotalItens,
                result.Pagina,
                result.ItensPorPagina));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationError("A paginação ou tipo de movimento é inválido.");
        }
    }

    private static async Task<IResult> ConsultarMovimentoAsync(
        Guid varianteId,
        Guid movimentoId,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId, movimentoId: movimentoId) is { } validation)
        {
            return validation;
        }

        var page = await consulta.ListarMovimentosAsync(
            new ListarMovimentosProdutoAcabadoFiltro(
                varianteId,
                Pagina: 1,
                ItensPorPagina: 1,
                MovimentoId: movimentoId),
            cancellationToken).ConfigureAwait(false);
        return page.Itens.Count == 0
            ? ResourceNotFound()
            : Results.Ok(MovimentoProdutoAcabadoAdminResponse.From(page.Itens[0]));
    }

    private static async Task<IResult> RegistrarEntradaAsync(
        Guid varianteId,
        EntradaProdutoAcabadoAdminRequest? request,
        HttpContext context,
        IRegistrarEntradaProdutoAcabado registrar,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        if (request is null)
        {
            return ValidationError("Os dados da entrada são obrigatórios.");
        }

        if (!TryGetResponsavel(context, out var responsavelId))
        {
            return Results.Unauthorized();
        }

        var result = await registrar.ExecuteAsync(
            new RegistrarEntradaProdutoAcabadoInput(
                varianteId,
                responsavelId,
                request.Tipo,
                request.Quantidade,
                request.CondicaoDestino,
                request.Motivo,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.LoteId,
                request.DataFabricacao,
                request.DataValidade,
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        if (!result.Sucesso)
        {
            return MapEntrada(result);
        }

        var movimento = result.Movimento!;
        var codigoLote = await ConsultarCodigoLoteAsync(
                varianteId,
                movimento.LoteId,
                consulta,
                cancellationToken)
            .ConfigureAwait(false);
        var response = MovimentoProdutoAcabadoAdminResponse.From(movimento, codigoLote);
        return result.Repetida
            ? Results.Ok(response)
            : Results.Created(
                $"{BasePath}/variantes/{varianteId:D}/movimentacoes/{response.Id:D}",
                response);
    }

    private static async Task<IResult> RegistrarSaidaAsync(
        Guid varianteId,
        SaidaProdutoAcabadoAdminRequest? request,
        HttpContext context,
        IRegistrarOperacaoProdutoAcabado registrar,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        if (request is null)
        {
            return ValidationError("Os dados da operação são obrigatórios.");
        }

        if (!TryGetResponsavel(context, out var responsavelId))
        {
            return Results.Unauthorized();
        }

        var result = await registrar.ExecuteAsync(
            new RegistrarOperacaoProdutoAcabadoInput(
                varianteId,
                request.LoteId,
                responsavelId,
                request.Tipo,
                request.Quantidade,
                request.CondicaoOrigem,
                null,
                request.Motivo,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        return await MapOperacao(varianteId, result, consulta, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> TransferirAsync(
        Guid varianteId,
        TransferenciaProdutoAcabadoAdminRequest? request,
        HttpContext context,
        IRegistrarOperacaoProdutoAcabado registrar,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        if (request is null)
        {
            return ValidationError("Os dados da transferência são obrigatórios.");
        }

        if (!TryGetResponsavel(context, out var responsavelId))
        {
            return Results.Unauthorized();
        }

        var result = await registrar.ExecuteAsync(
            new RegistrarOperacaoProdutoAcabadoInput(
                varianteId,
                request.LoteId,
                responsavelId,
                TipoMovimentoProdutoAcabado.TransferenciaCondicao,
                request.Quantidade,
                request.CondicaoOrigem,
                request.CondicaoDestino,
                request.Motivo,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        return await MapOperacao(varianteId, result, consulta, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> ContarAsync(
        Guid varianteId,
        ContagemProdutoAcabadoAdminRequest? request,
        HttpContext context,
        IContarProdutoAcabado contar,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId) is { } validation)
        {
            return validation;
        }

        if (request is null)
        {
            return ValidationError("Os dados da contagem são obrigatórios.");
        }

        if (!TryGetResponsavel(context, out var responsavelId))
        {
            return Results.Unauthorized();
        }

        var versaoSaldoEsperada = request.VersaoSaldoEsperada;
        if (string.IsNullOrWhiteSpace(versaoSaldoEsperada))
        {
            return ValidationError("A versão da posição é obrigatória e deve estar codificada em Base64.");
        }

        byte[] versao;
        try
        {
            versao = Convert.FromBase64String(versaoSaldoEsperada);
        }
        catch (FormatException)
        {
            return ValidationError("A versão da posição deve estar codificada em Base64.");
        }

        var result = await contar.ExecuteAsync(
            new ContarProdutoAcabadoInput(
                varianteId,
                request.LoteId,
                responsavelId,
                request.QuantidadeVendavel,
                request.QuantidadeDanificada,
                request.QuantidadeQuarentena,
                request.Motivo,
                context.Request.Headers["Idempotency-Key"].ToString(),
                versao,
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        if (result.Status == OperacaoProdutoAcabadoStatus.SemDiferenca)
        {
            return Results.Ok(new ContagemProdutoAcabadoAdminResponse(
                true,
                Array.Empty<MovimentoProdutoAcabadoAdminResponse>(),
                result.Repetida));
        }

        if (!result.Sucesso)
        {
            return MapContagem(result);
        }

        if (result.Movimentos.Count == 0)
        {
            throw new InvalidOperationException("A contagem foi registrada sem movimentos para compor a resposta.");
        }

        var loteId = result.Movimentos[0].LoteId;
        if (result.Movimentos.Any(movimento => movimento.LoteId != loteId))
        {
            throw new InvalidOperationException("Os movimentos da contagem pertencem a lotes diferentes.");
        }

        var codigoLote = await ConsultarCodigoLoteAsync(
                varianteId,
                loteId,
                consulta,
                cancellationToken)
            .ConfigureAwait(false);
        var movimentos = result.Movimentos
            .Select(movimento => MovimentoProdutoAcabadoAdminResponse.From(movimento, codigoLote))
            .ToArray();
        return result.Repetida
            ? Results.Ok(new ContagemProdutoAcabadoAdminResponse(false, movimentos, true))
            : Results.Created(
                $"{BasePath}/variantes/{varianteId:D}/movimentacoes",
                movimentos);
    }

    private static async Task<IResult> EstornarAsync(
        Guid varianteId,
        Guid movimentoId,
        EstornoProdutoAcabadoAdminRequest? request,
        HttpContext context,
        IEstornarMovimentoProdutoAcabado estornar,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (ValidateRouteIdentifiers(varianteId, movimentoId: movimentoId) is { } validation)
        {
            return validation;
        }

        if (request is null)
        {
            return ValidationError("Os dados do estorno são obrigatórios.");
        }

        if (!TryGetResponsavel(context, out var responsavelId))
        {
            return Results.Unauthorized();
        }

        var original = await consulta.ListarMovimentosAsync(
            new ListarMovimentosProdutoAcabadoFiltro(
                varianteId,
                Pagina: 1,
                ItensPorPagina: 1,
                MovimentoId: movimentoId),
            cancellationToken).ConfigureAwait(false);
        if (original.Itens.Count == 0)
        {
            return ResourceNotFound();
        }
        var codigoLote = original.Itens[0].CodigoLote;

        var result = await estornar.ExecuteAsync(
            new EstornarMovimentoProdutoAcabadoInput(
                movimentoId,
                responsavelId,
                request.Motivo,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        if (!result.Sucesso)
        {
            return MapEstorno(result);
        }

        var response = MovimentoProdutoAcabadoAdminResponse.From(result.Movimento!, codigoLote);
        return result.Repetida
            ? Results.Ok(response)
            : Results.Created(
                $"{BasePath}/variantes/{varianteId:D}/movimentacoes/{response.Id:D}",
                response);
    }

    private static async Task<IResult> MapOperacao(
        Guid varianteId,
        OperacaoProdutoAcabadoResult result,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        if (!result.Sucesso)
        {
            return result.Status switch
            {
                OperacaoProdutoAcabadoStatus.OperacaoInvalida => ValidationError(result.Erro),
                OperacaoProdutoAcabadoStatus.VarianteNaoEncontrada
                    or OperacaoProdutoAcabadoStatus.LoteNaoEncontrado
                    or OperacaoProdutoAcabadoStatus.LoteDeOutraVariante => ResourceNotFound(),
                _ => Conflict(result.Erro)
            };
        }

        var movimento = result.Movimento!;
        var codigoLote = await ConsultarCodigoLoteAsync(
                varianteId,
                movimento.LoteId,
                consulta,
                cancellationToken)
            .ConfigureAwait(false);
        var response = MovimentoProdutoAcabadoAdminResponse.From(movimento, codigoLote);
        return result.Repetida
            ? Results.Ok(response)
            : Results.Created(
                $"{BasePath}/variantes/{varianteId:D}/movimentacoes/{response.Id:D}",
                response);
    }

    private static IResult MapEntrada(RegistrarEntradaProdutoAcabadoResult result) =>
        result.Status switch
        {
            RegistrarEntradaProdutoAcabadoStatus.EntradaInvalida => ValidationError(result.Erro),
            RegistrarEntradaProdutoAcabadoStatus.VarianteNaoEncontrada
                or RegistrarEntradaProdutoAcabadoStatus.LoteNaoEncontrado => ResourceNotFound(),
            _ => Conflict(result.Erro)
        };

    private static IResult MapContagem(ContarProdutoAcabadoResult result) =>
        result.Status switch
        {
            OperacaoProdutoAcabadoStatus.OperacaoInvalida => ValidationError(result.Erro),
            OperacaoProdutoAcabadoStatus.VarianteNaoEncontrada
                or OperacaoProdutoAcabadoStatus.LoteNaoEncontrado => ResourceNotFound(),
            _ => Conflict(result.Erro)
        };

    private static IResult MapEstorno(EstornarMovimentoProdutoAcabadoResult result) =>
        result.Status switch
        {
            OperacaoProdutoAcabadoStatus.OperacaoInvalida => ValidationError(result.Erro),
            OperacaoProdutoAcabadoStatus.MovimentoNaoEncontrado
                or OperacaoProdutoAcabadoStatus.LoteNaoEncontrado => ResourceNotFound(),
            _ => Conflict(result.Erro)
        };

    private static IResult ValidationError(string? detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = [string.IsNullOrWhiteSpace(detail) ? "A requisição é inválida." : detail]
        });

    private static IResult? ValidateRouteIdentifiers(
        Guid varianteId,
        Guid? loteId = null,
        Guid? movimentoId = null)
    {
        if (varianteId == Guid.Empty)
        {
            return ValidationError("O identificador da variante não pode ser vazio.");
        }

        if (loteId == Guid.Empty)
        {
            return ValidationError("O identificador do lote não pode ser vazio.");
        }

        if (movimentoId == Guid.Empty)
        {
            return ValidationError("O identificador do movimento não pode ser vazio.");
        }

        return null;
    }

    private static async Task<string> ConsultarCodigoLoteAsync(
        Guid varianteId,
        Guid loteId,
        IConsultarEstoqueProdutoAcabado consulta,
        CancellationToken cancellationToken)
    {
        var lote = await consulta.ConsultarPosicaoLoteAsync(
                varianteId,
                loteId,
                cancellationToken)
            .ConfigureAwait(false);
        return lote?.Codigo
            ?? throw new InvalidOperationException("O lote da movimentação não foi encontrado para compor a resposta.");
    }

    private static IResult ResourceNotFound() =>
        Results.Problem(statusCode: 404, title: "Recurso não encontrado",
            detail: "O recurso solicitado não existe.");

    private static IResult Conflict(string? detail) =>
        Results.Problem(statusCode: 409, title: "Conflito no estoque",
            detail: string.IsNullOrWhiteSpace(detail)
                ? "A operação conflita com o estado atual do estoque."
                : detail);

    private static bool TryGetResponsavel(HttpContext context, out Guid responsavelId)
    {
        var value = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out responsavelId) && responsavelId != Guid.Empty;
    }
}

public sealed record PaginaAdminProdutoAcabadoResponse<T>(
    IReadOnlyList<T> Itens,
    int TotalItens,
    int Pagina,
    int ItensPorPagina);

public sealed record PoliticaProdutoAcabadoAdminRequest
{
    public int? EstoqueMinimo { get; init; }
    public int? PrazoValidadePadraoDias { get; init; }
}

public sealed record PoliticaProdutoAcabadoAdminResponse(
    Guid ProdutoVarianteId,
    int? EstoqueMinimo,
    int? PrazoValidadePadraoDias)
{
    public static PoliticaProdutoAcabadoAdminResponse From(
        PoliticaEstoqueProdutoVarianteConsulta politica) =>
        new(politica.ProdutoVarianteId, politica.EstoqueMinimo, politica.PrazoValidadePadraoDias);
}

public sealed record LoteProdutoAcabadoAdminResponse(
    Guid LoteId,
    string Codigo,
    DateOnly DataFabricacao,
    DateOnly DataValidade,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    OrigemValidadeLoteProdutoAcabado OrigemValidade,
    int? PrazoValidadeAplicadoDias,
    bool Valido,
    bool TemRegistroSaldo,
    string? VersaoSaldo,
    int QuantidadeVendavel,
    int QuantidadeDanificada,
    int QuantidadeQuarentena,
    int SaldoFisico,
    int SaldoUtilizavel,
    int SaldoReservado,
    int SaldoDisponivel)
{
    public static LoteProdutoAcabadoAdminResponse From(
        PosicaoLoteProdutoAcabadoConsulta lote) =>
        new(
            lote.LoteId,
            lote.Codigo,
            lote.DataFabricacao,
            lote.DataValidade,
            lote.OrigemValidade,
            lote.PrazoValidadeAplicadoDias,
            lote.Valido,
            lote.TemRegistroSaldo,
            lote.Versao is null ? null : Convert.ToBase64String(lote.Versao),
            lote.QuantidadeVendavel,
            lote.QuantidadeDanificada,
            lote.QuantidadeQuarentena,
            lote.SaldoFisico,
            lote.SaldoUtilizavel,
            lote.SaldoReservado,
            lote.SaldoDisponivel);
}

public sealed record PosicaoProdutoAcabadoAdminResponse(
    Guid ProdutoVarianteId,
    bool VarianteAtiva,
    int SaldoFisico,
    int SaldoUtilizavel,
    int SaldoReservado,
    int SaldoDisponivel,
    int? EstoqueMinimo,
    bool EstoqueMinimoConfigurado,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    SituacaoEstoqueProdutoAcabado Situacao,
    IReadOnlyList<LoteProdutoAcabadoAdminResponse> Lotes)
{
    public static PosicaoProdutoAcabadoAdminResponse From(
        DetalheEstoqueProdutoAcabadoConsulta detalhe) =>
        new(
            detalhe.Resumo.ProdutoVarianteId,
            detalhe.Resumo.VarianteAtiva,
            detalhe.Resumo.SaldoFisico,
            detalhe.Resumo.SaldoUtilizavel,
            detalhe.Resumo.SaldoReservado,
            detalhe.Resumo.SaldoDisponivel,
            detalhe.Resumo.EstoqueMinimo,
            detalhe.Resumo.EstoqueMinimoConfigurado,
            detalhe.Resumo.Situacao,
            detalhe.Lotes.Select(LoteProdutoAcabadoAdminResponse.From).ToArray());
}

public sealed record MovimentoProdutoAcabadoAdminResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    TipoMovimentoProdutoAcabado Tipo,
    int Quantidade,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    CondicaoEstoqueProdutoAcabado? CondicaoOrigem,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    CondicaoEstoqueProdutoAcabado? CondicaoDestino,
    int? QuantidadeContada,
    int SaldoAnteriorVendavel,
    int SaldoAnteriorDanificada,
    int SaldoAnteriorQuarentena,
    int SaldoPosteriorVendavel,
    int SaldoPosteriorDanificada,
    int SaldoPosteriorQuarentena,
    string Motivo,
    string? Observacao,
    Guid ResponsavelId,
    DateTimeOffset RegistradoEmUtc,
    Guid LoteId,
    string CodigoLote,
    Guid? MovimentoOriginalId)
{
    public static MovimentoProdutoAcabadoAdminResponse From(
        MovimentoProdutoAcabado movimento,
        string codigoLote) =>
        new(
            movimento.Id,
            movimento.Tipo,
            movimento.Quantidade,
            movimento.CondicaoOrigem,
            movimento.CondicaoDestino,
            movimento.QuantidadeContada,
            movimento.SaldoAnterior.QuantidadeVendavel,
            movimento.SaldoAnterior.QuantidadeDanificada,
            movimento.SaldoAnterior.QuantidadeQuarentena,
            movimento.SaldoPosterior.QuantidadeVendavel,
            movimento.SaldoPosterior.QuantidadeDanificada,
            movimento.SaldoPosterior.QuantidadeQuarentena,
            movimento.Motivo,
            movimento.Observacao,
            movimento.ResponsavelId,
            movimento.RegistradoEmUtc,
            movimento.LoteId,
            codigoLote,
            movimento.MovimentoOriginalId);

    public static MovimentoProdutoAcabadoAdminResponse From(
        MovimentoProdutoAcabadoConsulta movimento) =>
        new(
            movimento.MovimentoId,
            movimento.Tipo,
            movimento.Quantidade,
            movimento.CondicaoOrigem,
            movimento.CondicaoDestino,
            movimento.QuantidadeContada,
            movimento.SaldoAnterior.QuantidadeVendavel,
            movimento.SaldoAnterior.QuantidadeDanificada,
            movimento.SaldoAnterior.QuantidadeQuarentena,
            movimento.SaldoPosterior.QuantidadeVendavel,
            movimento.SaldoPosterior.QuantidadeDanificada,
            movimento.SaldoPosterior.QuantidadeQuarentena,
            movimento.Motivo,
            movimento.Observacao,
            movimento.ResponsavelId,
            movimento.RegistradoEmUtc,
            movimento.LoteId,
            movimento.CodigoLote,
            movimento.MovimentoOriginalId);
}

public sealed record EntradaProdutoAcabadoAdminRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoMovimentoProdutoAcabado Tipo { get; init; }
    public int Quantidade { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CondicaoEstoqueProdutoAcabado CondicaoDestino { get; init; }
    public Guid? LoteId { get; init; }
    public DateOnly? DataFabricacao { get; init; }
    public DateOnly? DataValidade { get; init; }
    public string? Motivo { get; init; }
    public string? Observacao { get; init; }
}

public sealed record SaidaProdutoAcabadoAdminRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoMovimentoProdutoAcabado Tipo { get; init; }
    public Guid LoteId { get; init; }
    public int Quantidade { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CondicaoEstoqueProdutoAcabado? CondicaoOrigem { get; init; }
    public string Motivo { get; init; } = string.Empty;
    public string? Observacao { get; init; }
}

public sealed record TransferenciaProdutoAcabadoAdminRequest
{
    public Guid LoteId { get; init; }
    public int Quantidade { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CondicaoEstoqueProdutoAcabado? CondicaoOrigem { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CondicaoEstoqueProdutoAcabado? CondicaoDestino { get; init; }
    public string Motivo { get; init; } = string.Empty;
    public string? Observacao { get; init; }
}

public sealed record ContagemProdutoAcabadoAdminRequest
{
    public Guid LoteId { get; init; }
    public int QuantidadeVendavel { get; init; }
    public int QuantidadeDanificada { get; init; }
    public int QuantidadeQuarentena { get; init; }
    public string Motivo { get; init; } = string.Empty;
    public string? VersaoSaldoEsperada { get; init; }
    public string? Observacao { get; init; }
}

public sealed record ContagemProdutoAcabadoAdminResponse(
    bool SemDiferenca,
    IReadOnlyList<MovimentoProdutoAcabadoAdminResponse> Movimentos,
    bool Repetida);

public sealed record EstornoProdutoAcabadoAdminRequest
{
    public string Motivo { get; init; } = string.Empty;
    public string? Observacao { get; init; }
}
