using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Api.Estoque;

public static class AdminEstoqueInsumoEndpoints
{
    public const string BasePath = "/api/admin/insumos";

    public static IEndpointRouteBuilder MapAdminEstoqueInsumoEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithTags("Estoque administrativo de insumos")
            .WithDescription("Acesso restrito a sessões autenticadas com o papel ADMIN.");

        group.MapGet("", ListarAsync)
            .WithName("ListAdminStockSupplies")
            .WithSummary("Lista insumos com filtros e paginação.")
            .Produces<PaginaAdminResponse<ResumoInsumoAdminResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{insumoId:guid}", ConsultarAsync)
            .WithName("GetAdminStockSupply")
            .WithSummary("Consulta os dados, formas de compra e posições do insumo.")
            .Produces<DetalheInsumoAdminResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{insumoId:guid}/posicao", ConsultarPosicaoAsync)
            .WithName("GetAdminStockSupplyPosition")
            .WithSummary("Consulta saldos físico, utilizável, reservado e disponível.")
            .Produces<PosicaoInsumoAdminResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{insumoId:guid}/lotes", ConsultarLotesAsync)
            .WithName("ListAdminStockSupplyLots")
            .WithSummary("Consulta lotes e elegibilidade sem ocultar quantidades inelegíveis.")
            .Produces<IReadOnlyList<PosicaoEstoqueAdminResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{insumoId:guid}/formas-compra", ListarFormasCompraAsync)
            .WithName("ListAdminStockSupplyPurchaseForms")
            .WithSummary("Lista formas de compra ativas e inativas do insumo.")
            .Produces<IReadOnlyList<FormaCompraAdminResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{insumoId:guid}/movimentacoes", ListarMovimentosAsync)
            .WithName("ListAdminStockSupplyMovements")
            .WithSummary("Lista o histórico paginado e filtrável do insumo.")
            .Produces<PaginaAdminResponse<MovimentoAdminResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{insumoId:guid}/movimentacoes/{movimentoId:guid}", ConsultarMovimentoAsync)
            .WithName("GetAdminStockSupplyMovement")
            .WithSummary("Consulta um movimento do histórico do insumo.")
            .Produces<MovimentoAdminResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CadastrarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("CreateAdminStockSupply")
            .WithSummary("Cadastra um insumo sem abrir saldo inicial.")
            .Produces<DetalheInsumoAdminResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{insumoId:guid}", AtualizarAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("UpdateAdminStockSupply")
            .WithSummary("Atualiza dados do insumo e protege mudanças de política com histórico.")
            .Produces<DetalheInsumoAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{insumoId:guid}/status", AlterarStatusAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminStockSupplyStatus")
            .WithSummary("Ativa ou inativa um insumo.")
            .Produces<DetalheInsumoAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{insumoId:guid}/formas-compra", CadastrarFormaCompraAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("CreateAdminStockSupplyPurchaseForm")
            .WithSummary("Cadastra uma forma de compra compatível com a unidade-base.")
            .Produces<FormaCompraAdminResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{insumoId:guid}/formas-compra/{formaCompraId:guid}", AtualizarFormaCompraAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("UpdateAdminStockSupplyPurchaseForm")
            .WithSummary("Atualiza dados de uma forma de compra sem alterar snapshots históricos.")
            .Produces<FormaCompraAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch(
                "/{insumoId:guid}/formas-compra/{formaCompraId:guid}/status",
                AlterarStatusFormaCompraAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("SetAdminStockSupplyPurchaseFormStatus")
            .WithSummary("Ativa ou inativa uma forma de compra.")
            .Produces<FormaCompraAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{insumoId:guid}/entradas", RegistrarEntradaAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("RegisterAdminStockSupplyEntry")
            .WithSummary("Registra estoque inicial ou aquisição com idempotência.")
            .Produces<MovimentoAdminResponse>(StatusCodes.Status201Created)
            .Produces<MovimentoAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{insumoId:guid}/saidas", RegistrarSaidaAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("RegisterAdminStockSupplyExit")
            .WithSummary("Registra saída manual, perda ou descarte.")
            .Produces<MovimentoAdminResponse>(StatusCodes.Status201Created)
            .Produces<MovimentoAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{insumoId:guid}/contagens", AjustarContagemAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("AdjustAdminStockSupplyCount")
            .WithSummary("Registra ajuste por contagem com rowversion da posição.")
            .Produces<MovimentoAdminResponse>(StatusCodes.Status201Created)
            .Produces<MovimentoAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost(
                "/{insumoId:guid}/movimentacoes/{movimentoId:guid}/estorno",
                EstornarMovimentoAsync)
            .RequireXaquiamuniAntiforgery()
            .WithName("ReverseAdminStockSupplyMovement")
            .WithSummary("Registra estorno compensatório de movimento elegível.")
            .Produces<MovimentoAdminResponse>(StatusCodes.Status201Created)
            .Produces<MovimentoAdminResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListarAsync(
        string? nome,
        bool? ativo,
        SituacaoEstoqueInsumo? situacao,
        int? pagina,
        int? itensPorPagina,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (nome?.Length > InsumoEstoque.NomeMaxLength
            || situacao is not null && !Enum.IsDefined(situacao.Value))
        {
            return ValidationError("Os filtros informados são inválidos.");
        }

        var requestedPage = pagina ?? 1;
        var pageSize = itensPorPagina ?? 20;
        try
        {
            var result = await consulta.ListarInsumosAsync(
                new ListarInsumosEstoqueFiltro(nome, ativo, situacao, requestedPage, pageSize),
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(new PaginaAdminResponse<ResumoInsumoAdminResponse>(
                result.Itens.Select(ResumoInsumoAdminResponse.From).ToArray(),
                result.TotalItens,
                result.Pagina,
                result.ItensPorPagina));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationError("A paginação ou situação informada é inválida.");
        }
    }

    private static async Task<IResult> ConsultarAsync(
        Guid insumoId,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (insumoId == Guid.Empty)
        {
            return ValidationError("O identificador do insumo é obrigatório.");
        }

        var result = await consulta.ConsultarInsumoAsync(insumoId, cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? ResourceNotFound()
            : Results.Ok(DetalheInsumoAdminResponse.From(result));
    }

    private static async Task<IResult> ConsultarPosicaoAsync(
        Guid insumoId,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (insumoId == Guid.Empty)
        {
            return ValidationError("O identificador do insumo é obrigatório.");
        }

        var result = await consulta.ConsultarInsumoAsync(insumoId, cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? ResourceNotFound()
            : Results.Ok(new PosicaoInsumoAdminResponse(
                ResumoInsumoAdminResponse.From(result.Resumo),
                result.Posicoes.Select(PosicaoEstoqueAdminResponse.From).ToArray()));
    }

    private static async Task<IResult> ConsultarLotesAsync(
        Guid insumoId,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (insumoId == Guid.Empty)
        {
            return ValidationError("O identificador do insumo é obrigatório.");
        }

        var result = await consulta.ConsultarInsumoAsync(insumoId, cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? ResourceNotFound()
            : Results.Ok(result.Posicoes
                .Where(posicao => posicao.LoteId is not null)
                .Select(PosicaoEstoqueAdminResponse.From)
                .ToArray());
    }

    private static async Task<IResult> ListarFormasCompraAsync(
        Guid insumoId,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (insumoId == Guid.Empty)
        {
            return ValidationError("O identificador do insumo é obrigatório.");
        }

        var result = await consulta.ConsultarFormasCompraAsync(insumoId, cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? ResourceNotFound()
            : Results.Ok(result.Select(FormaCompraAdminResponse.From).ToArray());
    }

    private static async Task<IResult> ListarMovimentosAsync(
        Guid insumoId,
        TipoMovimentoInsumo? tipo,
        Guid? loteId,
        int? pagina,
        int? itensPorPagina,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (insumoId == Guid.Empty
            || loteId == Guid.Empty
            || tipo is not null && !Enum.IsDefined(tipo.Value))
        {
            return ValidationError("Os filtros informados são inválidos.");
        }

        try
        {
            var result = await consulta.ListarMovimentosAsync(
                new ListarMovimentosInsumoFiltro(
                    insumoId,
                    tipo,
                    loteId,
                    pagina ?? 1,
                    itensPorPagina ?? 20),
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(new PaginaAdminResponse<MovimentoAdminResponse>(
                result.Itens.Select(MovimentoAdminResponse.From).ToArray(),
                result.TotalItens,
                result.Pagina,
                result.ItensPorPagina));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationError("A paginação ou tipo de movimento informado é inválido.");
        }
    }

    private static async Task<IResult> ConsultarMovimentoAsync(
        Guid insumoId,
        Guid movimentoId,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (insumoId == Guid.Empty || movimentoId == Guid.Empty)
        {
            return ValidationError("Os identificadores do insumo e do movimento são obrigatórios.");
        }

        var page = await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(insumoId, Pagina: 1, ItensPorPagina: 1, MovimentoId: movimentoId),
            cancellationToken).ConfigureAwait(false);
        return page.Itens.Count == 0
            ? ResourceNotFound()
            : Results.Ok(MovimentoAdminResponse.From(page.Itens[0]));
    }

    private static async Task<IResult> CadastrarAsync(
        CadastrarInsumoAdminRequest? request,
        IAdministrarInsumoEstoque administrar,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValidationError("Os dados do insumo são obrigatórios.");
        }

        var result = await administrar.CadastrarAsync(request.ToInput(), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Sucesso)
        {
            return MapAdministrarFailure(result);
        }

        var detalhe = await consulta.ConsultarInsumoAsync(result.Insumo!.Id, cancellationToken)
            .ConfigureAwait(false);
        return Results.Created(
            $"{BasePath}/{result.Insumo.Id:D}",
            DetalheInsumoAdminResponse.From(detalhe!));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid insumoId,
        AtualizarInsumoAdminRequest? request,
        IAdministrarInsumoEstoque administrar,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValidationError("Os dados do insumo são obrigatórios.");
        }

        var result = await administrar.AtualizarAsync(request.ToInput(insumoId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Sucesso)
        {
            return MapAdministrarFailure(result);
        }

        var detalhe = await consulta.ConsultarInsumoAsync(insumoId, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(DetalheInsumoAdminResponse.From(detalhe!));
    }

    private static async Task<IResult> AlterarStatusAsync(
        Guid insumoId,
        StatusInsumoAdminRequest? request,
        IAdministrarInsumoEstoque administrar,
        IConsultarEstoqueInsumo consulta,
        CancellationToken cancellationToken)
    {
        if (request?.Ativo is null)
        {
            return ValidationError("Informe o estado ativo do insumo.");
        }

        var result = await administrar.AlterarStatusAsync(insumoId, request.Ativo.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Sucesso)
        {
            return MapAdministrarFailure(result);
        }

        var detalhe = await consulta.ConsultarInsumoAsync(insumoId, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(DetalheInsumoAdminResponse.From(detalhe!));
    }

    private static async Task<IResult> CadastrarFormaCompraAsync(
        Guid insumoId,
        FormaCompraAdminRequest? request,
        IAdministrarInsumoEstoque administrar,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValidationError("Os dados da forma de compra são obrigatórios.");
        }

        var result = await administrar.CadastrarFormaCompraAsync(
            request.ToCadastrarInput(insumoId),
            cancellationToken).ConfigureAwait(false);
        return !result.Sucesso
            ? MapAdministrarFailure(result)
            : Results.Created(
                $"{BasePath}/{insumoId:D}/formas-compra/{result.FormaCompra!.Id:D}",
                FormaCompraAdminResponse.From(result.FormaCompra));
    }

    private static async Task<IResult> AtualizarFormaCompraAsync(
        Guid insumoId,
        Guid formaCompraId,
        FormaCompraAdminRequest? request,
        IAdministrarInsumoEstoque administrar,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValidationError("Os dados da forma de compra são obrigatórios.");
        }

        var result = await administrar.AtualizarFormaCompraAsync(
            request.ToAtualizarInput(insumoId, formaCompraId),
            cancellationToken).ConfigureAwait(false);
        return !result.Sucesso
            ? MapAdministrarFailure(result)
            : Results.Ok(FormaCompraAdminResponse.From(result.FormaCompra!));
    }

    private static async Task<IResult> AlterarStatusFormaCompraAsync(
        Guid insumoId,
        Guid formaCompraId,
        StatusFormaCompraAdminRequest? request,
        IAdministrarInsumoEstoque administrar,
        CancellationToken cancellationToken)
    {
        if (request?.Ativa is null)
        {
            return ValidationError("Informe o estado ativo da forma de compra.");
        }

        var result = await administrar.AlterarStatusFormaCompraAsync(
            insumoId,
            formaCompraId,
            request.Ativa.Value,
            cancellationToken).ConfigureAwait(false);
        return !result.Sucesso
            ? MapAdministrarFailure(result)
            : Results.Ok(FormaCompraAdminResponse.From(result.FormaCompra!));
    }

    private static async Task<IResult> RegistrarEntradaAsync(
        Guid insumoId,
        EntradaInsumoAdminRequest? request,
        HttpContext context,
        IRegistrarEntradaInsumo registrar,
        CancellationToken cancellationToken)
    {
        if (request is null || !TryGetResponsavel(context, out var responsavelId))
        {
            return request is null
                ? ValidationError("Os dados da entrada são obrigatórios.")
                : Results.Unauthorized();
        }

        var result = await registrar.ExecuteAsync(
            new RegistrarEntradaInsumoInput(
                insumoId,
                responsavelId,
                request.Tipo,
                request.QuantidadeOriginal,
                request.UnidadeOriginal,
                request.FormaCompraId,
                request.LoteId,
                request.IdentificacaoNovoLote,
                request.DataEntradaNovoLote,
                request.DataValidadeNovoLote,
                request.FornecedorNovoLote,
                request.NovoLoteBloqueado,
                request.CustoTotal,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao, request.SemControleValidade),
            cancellationToken).ConfigureAwait(false);
        if (!result.Sucesso)
        {
            return MapEntradaFailure(result);
        }

        var response = MovimentoAdminResponse.From(result.Movimento!);
        return result.Repetida
            ? Results.Ok(response)
            : Results.Created(
                $"{BasePath}/{insumoId:D}/movimentacoes/{response.Id:D}",
                response);
    }

    private static async Task<IResult> RegistrarSaidaAsync(
        Guid insumoId,
        SaidaInsumoAdminRequest? request,
        HttpContext context,
        IRegistrarSaidaInsumo registrar,
        CancellationToken cancellationToken)
    {
        if (request is null || !TryGetResponsavel(context, out var responsavelId))
        {
            return request is null
                ? ValidationError("Os dados da saída são obrigatórios.")
                : Results.Unauthorized();
        }

        var result = await registrar.ExecutarAsync(
            new RegistrarSaidaInsumoInput(
                insumoId,
                responsavelId,
                request.Tipo,
                request.QuantidadeOriginal,
                request.UnidadeOriginal,
                request.LoteId,
                request.Motivo ?? string.Empty,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        return MapOperacaoSucesso(result, insumoId);
    }

    private static async Task<IResult> AjustarContagemAsync(
        Guid insumoId,
        ContagemInsumoAdminRequest? request,
        HttpContext context,
        IConsultarEstoqueInsumo consulta,
        IAjustarContagemInsumo ajustar,
        CancellationToken cancellationToken)
    {
        if (request is null || !TryGetResponsavel(context, out var responsavelId))
        {
            return request is null
                ? ValidationError("Os dados da contagem são obrigatórios.")
                : Results.Unauthorized();
        }

        byte[]? rowVersion;
        try
        {
            rowVersion = string.IsNullOrEmpty(request.VersaoSaldoEsperada)
                ? null
                : Convert.FromBase64String(request.VersaoSaldoEsperada);
        }
        catch (FormatException)
        {
            return ValidationError("A versão da posição deve estar codificada em Base64.");
        }

        if (rowVersion is { Length: not 8 })
        {
            return ValidationError("A versão da posição informada é inválida.");
        }

        var result = await ajustar.ExecutarAsync(
            new AjustarContagemInsumoInput(
                insumoId,
                responsavelId,
                request.QuantidadeEncontrada,
                request.UnidadeOriginal,
                request.LoteId,
                rowVersion,
                request.Motivo ?? string.Empty,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        return MapOperacaoSucesso(result, insumoId);
    }

    private static async Task<IResult> EstornarMovimentoAsync(
        Guid insumoId,
        Guid movimentoId,
        EstornoInsumoAdminRequest? request,
        HttpContext context,
        IConsultarEstoqueInsumo consulta,
        IEstornarMovimentoInsumo estornar,
        CancellationToken cancellationToken)
    {
        if (request is null || !TryGetResponsavel(context, out var responsavelId))
        {
            return request is null
                ? ValidationError("Os dados do estorno são obrigatórios.")
                : Results.Unauthorized();
        }

        if (insumoId == Guid.Empty || movimentoId == Guid.Empty)
        {
            return ValidationError("Os identificadores do insumo e do movimento são obrigatórios.");
        }

        var original = await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(
                insumoId,
                Pagina: 1,
                ItensPorPagina: 1,
                MovimentoId: movimentoId),
            cancellationToken).ConfigureAwait(false);
        if (original.Itens.Count == 0)
        {
            return ResourceNotFound();
        }

        var result = await estornar.ExecutarAsync(
            new EstornarMovimentoInsumoInput(
                movimentoId,
                responsavelId,
                request.Motivo ?? string.Empty,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request.Observacao),
            cancellationToken).ConfigureAwait(false);
        return MapOperacaoSucesso(result, insumoId);
    }

    private static IResult MapOperacaoSucesso(OperacaoEstoqueInsumoResult result, Guid insumoId)
    {
        if (result.Status == OperacaoEstoqueInsumoStatus.SemDiferenca)
        {
            return Results.Ok(new ContagemSemDiferencaAdminResponse());
        }

        if (!result.Sucesso)
        {
            return MapOperacaoFailure(result);
        }

        var response = MovimentoAdminResponse.From(result.Movimento!);
        return result.Repetida
            ? Results.Ok(response)
            : Results.Created(
                $"{BasePath}/{insumoId:D}/movimentacoes/{response.Id:D}",
                response);
    }

    private static IResult MapAdministrarFailure(AdministrarInsumoEstoqueResult result) =>
        result.Status switch
        {
            AdministrarInsumoEstoqueStatus.DadosInvalidos => ValidationError(result.Erro),
            AdministrarInsumoEstoqueStatus.InsumoNaoEncontrado
                or AdministrarInsumoEstoqueStatus.FormaCompraNaoEncontrada => ResourceNotFound(),
            AdministrarInsumoEstoqueStatus.HistoricoImpedeAlteracao
                or AdministrarInsumoEstoqueStatus.Conflito => Conflict(result.Erro),
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                "Unsupported stock supply administration result.")
        };

    private static IResult MapEntradaFailure(RegistrarEntradaInsumoResult result) =>
        result.Status switch
        {
            RegistrarEntradaInsumoStatus.EntradaInvalida => ValidationError(result.Erro),
            RegistrarEntradaInsumoStatus.InsumoNaoEncontrado
                or RegistrarEntradaInsumoStatus.LoteNaoEncontrado => ResourceNotFound(),
            RegistrarEntradaInsumoStatus.InsumoInativo
                or RegistrarEntradaInsumoStatus.LoteInelegivel
                or RegistrarEntradaInsumoStatus.ConflitoIdempotencia
                or RegistrarEntradaInsumoStatus.ConflitoConcorrencia => Conflict(result.Erro),
            _ => throw new ArgumentOutOfRangeException(nameof(result), "Unsupported stock entry result.")
        };

    private static IResult MapOperacaoFailure(OperacaoEstoqueInsumoResult result) =>
        result.Status switch
        {
            OperacaoEstoqueInsumoStatus.OperacaoInvalida => ValidationError(result.Erro),
            OperacaoEstoqueInsumoStatus.InsumoNaoEncontrado
                or OperacaoEstoqueInsumoStatus.LoteNaoEncontrado
                or OperacaoEstoqueInsumoStatus.MovimentoNaoEncontrado => ResourceNotFound(),
            OperacaoEstoqueInsumoStatus.LoteInelegivel
                or OperacaoEstoqueInsumoStatus.SaldoInsuficiente
                or OperacaoEstoqueInsumoStatus.MovimentoJaEstornado
                or OperacaoEstoqueInsumoStatus.MovimentoVinculadoCompra
                or OperacaoEstoqueInsumoStatus.ConflitoIdempotencia
                or OperacaoEstoqueInsumoStatus.ConflitoConcorrencia => Conflict(result.Erro),
            _ => throw new ArgumentOutOfRangeException(nameof(result), "Unsupported stock operation result.")
        };

    private static IResult ValidationError(string? detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = [string.IsNullOrWhiteSpace(detail) ? "A requisição é inválida." : detail]
        });

    private static IResult ResourceNotFound() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Recurso não encontrado",
            detail: "O recurso solicitado não existe.");

    private static IResult Conflict(string? detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflito no estoque",
            detail: string.IsNullOrWhiteSpace(detail)
                ? "A operação conflita com o estado atual do estoque."
                : detail);

    private static bool TryGetResponsavel(HttpContext context, out Guid responsavelId)
    {
        var value = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out responsavelId) && responsavelId != Guid.Empty;
    }
}

/// <summary>Parâmetros de paginação retornados em listagens administrativas.</summary>
public sealed record PaginaAdminResponse<T>(
    IReadOnlyList<T> Itens,
    int TotalItens,
    int Pagina,
    int ItensPorPagina);

/// <summary>Resumo do cadastro e dos saldos de um insumo.</summary>
public sealed record ResumoInsumoAdminResponse(
    Guid InsumoId,
    string Nome,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TipoInsumoEstoque Tipo,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] UnidadeFisicaEstoque UnidadeBase,
    bool Ativo,
    bool ControlaLote,
    bool ValidadeObrigatoria,
    decimal SaldoFisico,
    decimal SaldoUtilizavel,
    decimal SaldoReservado,
    decimal SaldoDisponivel,
    decimal? EstoqueMinimo,
    bool EstoqueMinimoConfigurado,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SituacaoEstoqueInsumo Situacao)
{
    public static ResumoInsumoAdminResponse From(ResumoEstoqueInsumo resumo) =>
        new(
            resumo.InsumoId,
            resumo.Nome,
            resumo.Tipo,
            resumo.UnidadeBase,
            resumo.Ativo,
            resumo.ControlaLote,
            resumo.ValidadeObrigatoria,
            resumo.SaldoFisico,
            resumo.SaldoUtilizavel,
            resumo.SaldoReservado,
            resumo.SaldoDisponivel,
            resumo.EstoqueMinimo,
            resumo.EstoqueMinimoConfigurado,
            resumo.Situacao);
}

/// <summary>Uma forma de aquisição configurada para o insumo.</summary>
public sealed record FormaCompraAdminResponse(
    Guid Id,
    string Nome,
    decimal QuantidadeEquivalentePorEmbalagem,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] UnidadeFisicaEstoque Unidade,
    bool Ativa)
{
    public static FormaCompraAdminResponse From(FormaCompraInsumoConsulta forma) =>
        new(
            forma.Id,
            forma.Nome,
            forma.QuantidadeEquivalentePorEmbalagem,
            forma.UnidadeBase,
            forma.Ativa);

    public static FormaCompraAdminResponse From(FormaCompraInsumo forma) =>
        new(
            forma.Id,
            forma.Nome,
            forma.QuantidadeEquivalentePorEmbalagem.ValorBase,
            forma.QuantidadeEquivalentePorEmbalagem.UnidadeBase,
            forma.Ativa);
}

/// <summary>Posição física agregada ou vinculada a um lote.</summary>
public sealed record PosicaoEstoqueAdminResponse(
    Guid? LoteId,
    string? IdentificacaoLote,
    string? Fornecedor,
    DateOnly? DataEntrada,
    DateOnly? DataValidade,
    bool? Bloqueado,
    bool Elegivel,
    bool TemRegistroSaldo,
    string? VersaoSaldo,
    decimal SaldoFisico,
    decimal SaldoUtilizavel,
    decimal SaldoReservado,
    decimal SaldoDisponivel)
{
    public static PosicaoEstoqueAdminResponse From(PosicaoEstoqueInsumoConsulta posicao) =>
        new(
            posicao.LoteId,
            posicao.IdentificacaoLote,
            posicao.Fornecedor,
            posicao.DataEntrada,
            posicao.DataValidade,
            posicao.Bloqueado,
            posicao.Elegivel,
            posicao.TemRegistroSaldo,
            posicao.Versao is null ? null : Convert.ToBase64String(posicao.Versao),
            posicao.SaldoFisico,
            posicao.SaldoUtilizavel,
            posicao.SaldoReservado,
            posicao.SaldoDisponivel);
}

/// <summary>Detalhes administrativos de um insumo, incluindo as suas posições.</summary>
public sealed record DetalheInsumoAdminResponse(
    ResumoInsumoAdminResponse Resumo,
    IReadOnlyList<FormaCompraAdminResponse> FormasCompra,
    IReadOnlyList<PosicaoEstoqueAdminResponse> Posicoes)
{
    public static DetalheInsumoAdminResponse From(DetalheEstoqueInsumo detalhe) =>
        new(
            ResumoInsumoAdminResponse.From(detalhe.Resumo),
            detalhe.FormasCompra.Select(FormaCompraAdminResponse.From).ToArray(),
            detalhe.Posicoes.Select(PosicaoEstoqueAdminResponse.From).ToArray());
}

/// <summary>Resumo dos saldos e posições do insumo, sem dados de cadastro adicionais.</summary>
public sealed record PosicaoInsumoAdminResponse(
    ResumoInsumoAdminResponse Resumo,
    IReadOnlyList<PosicaoEstoqueAdminResponse> Posicoes);

/// <summary>Movimento do ledger sem dados internos de idempotência ou fingerprint.</summary>
public sealed record MovimentoAdminResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TipoMovimentoInsumo Tipo,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DirecaoMovimentoInsumo Direcao,
    decimal QuantidadeOriginal,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] UnidadeFisicaEstoque? UnidadeOriginal,
    decimal QuantidadeBase,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] UnidadeFisicaEstoque UnidadeBase,
    decimal SaldoAnterior,
    decimal SaldoPosterior,
    string Motivo,
    string? Observacao,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OrigemMovimentoInsumo Origem,
    Guid ResponsavelId,
    DateTimeOffset RegistradoEmUtc,
    Guid? LoteId,
    string? IdentificacaoLote,
    Guid? FormaCompraId,
    string? FormaCompraNome,
    decimal? FatorCompra,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] UnidadeFisicaEstoque? UnidadeFatorCompra,
    decimal? CustoTotal,
    decimal? CustoPorUnidadeBase,
    Guid? MovimentoOriginalId,
    Guid? OrdemProducaoId = null)
{
    public static MovimentoAdminResponse From(MovimentoInsumo movimento) =>
        new(
            movimento.Id,
            movimento.Tipo,
            movimento.Direcao,
            movimento.QuantidadeOriginal,
            movimento.UnidadeOriginal,
            movimento.QuantidadeBase.ValorBase,
            movimento.QuantidadeBase.UnidadeBase,
            movimento.SaldoAnterior.ValorBase,
            movimento.SaldoPosterior.ValorBase,
            movimento.Motivo,
            movimento.Observacao,
            ObterOrigem(movimento),
            movimento.ResponsavelId,
            movimento.RegistradoEmUtc,
            movimento.LoteId,
            null,
            movimento.FormaCompraId,
            movimento.FormaCompraNome,
            movimento.FatorCompra?.ValorBase,
            movimento.FatorCompra?.UnidadeBase,
            movimento.CustoTotal,
            movimento.CustoPorUnidadeBase,
            movimento.MovimentoOriginalId);

    public static MovimentoAdminResponse From(MovimentoInsumoConsulta movimento) =>
        new(
            movimento.Id,
            movimento.Tipo,
            movimento.Direcao,
            movimento.QuantidadeOriginal,
            movimento.UnidadeOriginal,
            movimento.QuantidadeBase,
            movimento.UnidadeBase,
            movimento.SaldoAnterior,
            movimento.SaldoPosterior,
            movimento.Motivo,
            movimento.Observacao,
            movimento.Origem,
            movimento.ResponsavelId,
            movimento.RegistradoEmUtc,
            movimento.LoteId,
            movimento.IdentificacaoLote,
            movimento.FormaCompraId,
            movimento.FormaCompraNome,
            movimento.FatorCompra,
            movimento.UnidadeFatorCompra,
            null,
            null,
            movimento.MovimentoOriginalId,
            movimento.OrdemProducaoId);

    private static OrigemMovimentoInsumo ObterOrigem(MovimentoInsumo movimento) =>
        movimento.Tipo switch
        {
            TipoMovimentoInsumo.EstoqueInicial or TipoMovimentoInsumo.Aquisicao =>
                movimento.FormaCompraId is null
                    ? OrigemMovimentoInsumo.UnidadeFisica
                    : OrigemMovimentoInsumo.FormaCompra,
            TipoMovimentoInsumo.AjusteInventario => OrigemMovimentoInsumo.ContagemFisica,
            TipoMovimentoInsumo.Estorno => OrigemMovimentoInsumo.MovimentoOriginal,
            _ => OrigemMovimentoInsumo.OperacaoManual
        };
}

/// <summary>Cadastro de um insumo, com estoque mínimo na unidade-base.</summary>
public sealed record CadastrarInsumoAdminRequest
{
    /// <summary>Nome do insumo.</summary>
    public string? Nome { get; init; }

    /// <summary>Classificação do insumo.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoInsumoEstoque Tipo { get; init; }

    /// <summary>Unidade física-base, limitada a grama, mililitro ou unidade.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnidadeFisicaEstoque UnidadeBase { get; init; }

    /// <summary>Estoque mínimo na unidade-base; nulo significa não configurado.</summary>
    public decimal? EstoqueMinimo { get; init; }

    /// <summary>Indica se as posições serão controladas por lote.</summary>
    public bool ControlaLote { get; init; }

    /// <summary>Indica se cada lote deve informar validade.</summary>
    public bool ValidadeObrigatoria { get; init; }

    public CadastrarInsumoEstoqueInput ToInput() =>
        new(Nome, Tipo, UnidadeBase, EstoqueMinimo, ControlaLote, ValidadeObrigatoria);
}

/// <summary>Atualização dos dados cadastrais permitidos para um insumo.</summary>
public sealed record AtualizarInsumoAdminRequest
{
    /// <summary>Novo nome do insumo.</summary>
    public string? Nome { get; init; }

    /// <summary>Nova classificação do insumo.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoInsumoEstoque Tipo { get; init; }

    /// <summary>Novo estoque mínimo na unidade-base; nulo remove o mínimo.</summary>
    public decimal? EstoqueMinimo { get; init; }

    /// <summary>Indica se as posições serão controladas por lote.</summary>
    public bool ControlaLote { get; init; }

    /// <summary>Indica se cada lote deve informar validade.</summary>
    public bool ValidadeObrigatoria { get; init; }

    public AtualizarInsumoEstoqueInput ToInput(Guid insumoId) =>
        new(insumoId, Nome, Tipo, EstoqueMinimo, ControlaLote, ValidadeObrigatoria);
}

/// <summary>Solicita a ativação ou inativação do insumo.</summary>
public sealed record StatusInsumoAdminRequest
{
    /// <summary>Novo estado ativo.</summary>
    public bool? Ativo { get; init; }
}

/// <summary>Cadastro ou atualização do fator de uma forma de compra.</summary>
public sealed record FormaCompraAdminRequest
{
    /// <summary>Nome de exibição da forma de compra.</summary>
    public string? Nome { get; init; }

    /// <summary>Quantidade física equivalente a uma embalagem.</summary>
    public decimal QuantidadeEquivalentePorEmbalagem { get; init; }

    /// <summary>Unidade da quantidade equivalente.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnidadeFisicaEstoque Unidade { get; init; }

    public CadastrarFormaCompraInsumoInput ToCadastrarInput(Guid insumoId) =>
        new(insumoId, Nome, QuantidadeEquivalentePorEmbalagem, Unidade);

    public AtualizarFormaCompraInsumoInput ToAtualizarInput(Guid insumoId, Guid formaCompraId) =>
        new(insumoId, formaCompraId, Nome, QuantidadeEquivalentePorEmbalagem, Unidade);
}

/// <summary>Solicita a ativação ou inativação da forma de compra.</summary>
public sealed record StatusFormaCompraAdminRequest
{
    /// <summary>Novo estado ativo.</summary>
    public bool? Ativa { get; init; }
}

/// <summary>Dados de uma entrada inicial ou aquisição de insumo.</summary>
public sealed record EntradaInsumoAdminRequest
{
    /// <summary>Tipo de entrada: estoque inicial ou aquisição.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoMovimentoInsumo Tipo { get; init; }

    /// <summary>Quantidade informada diretamente ou número de embalagens.</summary>
    public decimal QuantidadeOriginal { get; init; }

    /// <summary>Unidade direta; nula quando uma forma de compra é usada.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnidadeFisicaEstoque? UnidadeOriginal { get; init; }

    /// <summary>Forma de compra opcional do insumo.</summary>
    public Guid? FormaCompraId { get; init; }

    /// <summary>Lote existente, quando a política do insumo exige lote.</summary>
    public Guid? LoteId { get; init; }

    /// <summary>Identificação de lote novo, quando criar lote junto da entrada.</summary>
    public string? IdentificacaoNovoLote { get; init; }

    /// <summary>Data de entrada do lote novo.</summary>
    public DateOnly? DataEntradaNovoLote { get; init; }

    /// <summary>Data de validade de calendário do lote novo.</summary>
    public DateOnly? DataValidadeNovoLote { get; init; }
    public bool SemControleValidade { get; init; }

    /// <summary>Fornecedor opcional do lote novo.</summary>
    public string? FornecedorNovoLote { get; init; }

    /// <summary>Indica se o lote novo será bloqueado.</summary>
    public bool NovoLoteBloqueado { get; init; }

    /// <summary>Custo total de aquisição opcional em BRL.</summary>
    public decimal? CustoTotal { get; init; }

    /// <summary>Observação opcional do movimento.</summary>
    public string? Observacao { get; init; }
}

/// <summary>Saída manual, perda ou descarte de insumo.</summary>
public sealed record SaidaInsumoAdminRequest
{
    /// <summary>Tipo de saída permitido nesta etapa.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoMovimentoInsumo Tipo { get; init; }

    /// <summary>Quantidade física a retirar.</summary>
    public decimal QuantidadeOriginal { get; init; }

    /// <summary>Unidade da quantidade.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnidadeFisicaEstoque UnidadeOriginal { get; init; }

    /// <summary>Lote explícito para insumos controlados por lote.</summary>
    public Guid? LoteId { get; init; }

    /// <summary>Motivo obrigatório e rastreável.</summary>
    public string? Motivo { get; init; }

    /// <summary>Observação opcional.</summary>
    public string? Observacao { get; init; }
}

/// <summary>Contagem física de uma posição agregada ou de um lote.</summary>
public sealed record ContagemInsumoAdminRequest
{
    /// <summary>Quantidade física realmente encontrada.</summary>
    public decimal QuantidadeEncontrada { get; init; }

    /// <summary>Unidade da contagem.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnidadeFisicaEstoque UnidadeOriginal { get; init; }

    /// <summary>Lote cuja quantidade foi contada, obrigatório para controle por lote.</summary>
    public Guid? LoteId { get; init; }

    /// <summary>Versão Base64 retornada pela posição consultada; nula se ainda não há saldo.</summary>
    public string? VersaoSaldoEsperada { get; init; }

    /// <summary>Motivo obrigatório e rastreável.</summary>
    public string? Motivo { get; init; }

    /// <summary>Observação opcional.</summary>
    public string? Observacao { get; init; }
}

/// <summary>Motivo do estorno compensatório.</summary>
public sealed record EstornoInsumoAdminRequest
{
    /// <summary>Motivo obrigatório do estorno.</summary>
    public string? Motivo { get; init; }

    /// <summary>Observação opcional.</summary>
    public string? Observacao { get; init; }
}

/// <summary>Indica que a contagem coincide com o saldo e não gerou movimento.</summary>
public sealed record ContagemSemDiferencaAdminResponse;
