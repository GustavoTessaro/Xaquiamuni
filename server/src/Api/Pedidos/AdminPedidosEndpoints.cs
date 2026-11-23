using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Pedidos;

public static class AdminPedidosEndpoints
{
    public const string BasePath = "/api/admin/pedidos";
    private const int TamanhoPaginaMaximo = 50;
    private const int MaxBytesComando = 4 * 1024;
    private static readonly JsonSerializerOptions JsonComando = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    public static IEndpointRouteBuilder MapAdminPedidosEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthorizationPolicies.Administracao)
            .WithTags("Pedidos administrativos");

        group.MapGet("", ListarAsync).WithName("ListarPedidosAdmin")
            .Produces<PedidoAdminPaginaResponse>().ProducesValidationProblem().Produces(401).Produces(403);
        group.MapGet("/{pedidoId:guid}", BuscarAsync).WithName("BuscarPedidoAdmin")
            .Produces<PedidoAdminResponse>().ProducesValidationProblem().Produces(401).Produces(403).Produces(404);
        group.MapGet("/{pedidoId:guid}/historico-rastreamento", ConsultarHistoricoRastreamentoAsync)
            .WithName("ConsultarHistoricoRastreamentoAdmin")
            .Produces<IReadOnlyList<CorrecaoRastreamentoAdmin>>().Produces(401).Produces(403).Produces(404);
        group.MapPost("/{pedidoId:guid}/confirmar-pagamento", ConfirmarAsync).WithName("ConfirmarPagamentoAdmin")
            .WithSummary("Registra a confirmação manual após a conferência do recebimento no banco.")
            .RequireXaquiamuniAntiforgery().Produces<ResultadoPedidoAdminResponse>()
            .ProducesValidationProblem().Produces(401).Produces(403).Produces(404)
            .ProducesProblem(409).ProducesProblem(413).ProducesProblem(415);
        group.MapPost("/{pedidoId:guid}/cancelar", CancelarAsync).WithName("CancelarPedidoAdmin")
            .RequireXaquiamuniAntiforgery().Produces<ResultadoPedidoAdminResponse>()
            .ProducesValidationProblem().Produces(401).Produces(403).Produces(404)
            .ProducesProblem(409).ProducesProblem(413).ProducesProblem(415);

        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/iniciar-preparacao", IniciarPreparacaoAsync)
            .WithName("IniciarPreparacaoAdmin"));
        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/liberar-retirada", LiberarRetiradaAsync)
            .WithName("LiberarRetiradaAdmin"));
        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/confirmar-entrega", ConfirmarEntregaAsync)
            .WithName("ConfirmarEntregaAdmin"));
        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/iniciar-entrega-propria",
            (HttpContext ctx, Guid pedidoId, IEntregaPropriaPedido entrega, CancellationToken ct) =>
                EntregaPropriaAsync(ctx, pedidoId, entrega, EtapaEntregaPropria.Iniciar, ct)));
        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/confirmar-recebimento",
            (HttpContext ctx, Guid pedidoId, IEntregaPropriaPedido entrega, CancellationToken ct) =>
                EntregaPropriaAsync(ctx, pedidoId, entrega, EtapaEntregaPropria.ConfirmarRecebimento, ct)));
        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/registrar-postagem",
            (HttpContext ctx, Guid pedidoId, IEntregaPostal entrega, CancellationToken ct) =>
                EntregaPostalAsync<RegistrarPostagemRequest>(ctx, pedidoId, entrega, EtapaEntregaPostal.Postar, ct)));
        ConfigurarComandoOperacional(group.MapPut("/{pedidoId:guid}/rastreamento-postal",
            (HttpContext ctx, Guid pedidoId, IEntregaPostal entrega, CancellationToken ct) =>
                EntregaPostalAsync<CorrigirRastreamentoPostalRequest>(ctx, pedidoId, entrega, EtapaEntregaPostal.CorrigirRastreamento, ct)));
        ConfigurarComandoOperacional(group.MapPost("/{pedidoId:guid}/confirmar-entrega-postal",
            (HttpContext ctx, Guid pedidoId, IEntregaPostal entrega, CancellationToken ct) =>
                EntregaPostalAsync<ConfirmarEntregaPostalRequest>(ctx, pedidoId, entrega, EtapaEntregaPostal.ConfirmarRecebimento, ct)));

        return endpoints;
    }

    private static async Task<IResult> ListarAsync(
        HttpContext context, int? pagina, int? itensPorPagina, string? situacao,
        IConsultarPedidosAdmin consulta, CancellationToken cancellationToken)
    {
        if (!TentarObterAdministrador(context, out _))
        {
            return Results.Unauthorized();
        }

        var numero = pagina ?? 1;
        var tamanho = itensPorPagina ?? 20;
        if (numero < 1 || tamanho is < 1 or > TamanhoPaginaMaximo
            || (numero - 1L) * tamanho > int.MaxValue)
        {
            return Invalido("consulta", $"Informe uma página positiva e entre 1 e {TamanhoPaginaMaximo} itens por página.");
        }

        SituacaoPedido? filtro = null;
        if (situacao is not null)
        {
            if (!Enum.TryParse<SituacaoPedido>(situacao, ignoreCase: true, out var valor)
                || !string.Equals(valor.ToString(), situacao, StringComparison.OrdinalIgnoreCase)
                || !Enum.IsDefined(valor))
            {
                return Invalido("situacao", "Informe um nome de situação de pedido válido.");
            }

            filtro = valor;
        }

        var resultado = await consulta.ListarAsync(filtro, (numero - 1) * tamanho, tamanho, cancellationToken)
            .ConfigureAwait(false);
        var itens = resultado.Itens.Select(pedido => new PedidoAdminResumoResponse(pedido.Id,
            pedido.Situacao.ToString(), pedido.CriadoEmUtc, pedido.PrazoPagamentoEmUtc, pedido.TotalCentavos,
            pedido.NomeCliente)).ToArray();
        var totalPaginas = (int)((resultado.TotalItens + tamanho - 1L) / tamanho);
        return Results.Ok(new PedidoAdminPaginaResponse(itens, numero, tamanho, resultado.TotalItens, totalPaginas));
    }

    private static async Task<IResult> BuscarAsync(
        HttpContext context, Guid pedidoId, IConsultarPedidosAdmin consulta, CancellationToken cancellationToken)
    {
        if (!TentarObterAdministrador(context, out _))
        {
            return Results.Unauthorized();
        }
        if (pedidoId == Guid.Empty)
        {
            return Invalido("pedidoId", "Informe um identificador de pedido válido.");
        }

        var detalhe = await consulta.BuscarAsync(pedidoId, cancellationToken).ConfigureAwait(false);
        if (detalhe?.Pedido.Versao is { Length: 8 } versao)
            context.Response.Headers.ETag = $"\"{Convert.ToBase64String(versao)}\"";
        return detalhe is null ? Results.NotFound() : Results.Ok(new PedidoAdminResponse(
            PedidoClienteResponse.From(detalhe.Pedido), new(detalhe.NomeCliente, detalhe.EmailCliente)));
    }

    private static async Task<IResult> ConsultarHistoricoRastreamentoAsync(
        HttpContext context, Guid pedidoId, IConsultarPedidosAdmin consulta, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!TentarObterAdministrador(context, out _))
            return Results.Unauthorized();

        var historico = await consulta.ConsultarHistoricoRastreamentoAsync(pedidoId, cancellationToken);
        return historico is null ? Results.NotFound() : Results.Ok(historico);
    }

    private static async Task<IResult> EntregaPropriaAsync(HttpContext ctx, Guid pedidoId,
        IEntregaPropriaPedido entrega, EtapaEntregaPropria etapa, CancellationToken ct)
    {
        if (!TentarObterAdministrador(ctx, out var administrador))
            return Results.Unauthorized();
        var precondicao = LerPrecondicaoOperacional(ctx, out var chave, out var versao);
        if (precondicao is not null) return precondicao;
        var (comando, erro) = await LerComandoAsync<TransicaoEntregaPropriaRequest>(ctx.Request, ct);
        if (erro is not null)
            return erro;
        if (comando?.Confirmado != true)
            return Invalido("confirmado", etapa == EtapaEntregaPropria.Iniciar
                ? "Confirme que a preparação foi concluída e os produtos saíram da loja."
                : "Confirme que o cliente recebeu os produtos.");
        var resultado = await entrega.ExecutarAsync(new(pedidoId, administrador, chave, versao, etapa), ct);
        return resultado.Status switch
        {
            StatusEntregaPropria.Sucesso => Atualizado(pedidoId, resultado.Situacao!.Value, resultado.Repetida),
            StatusEntregaPropria.AdministradorNaoAutorizado => Results.Forbid(),
            StatusEntregaPropria.PedidoNaoEncontrado => Results.NotFound(),
            StatusEntregaPropria.ConflitoConcorrencia => ConflitoConcorrencia(),
            StatusEntregaPropria.VersaoDivergente => Conflito("versao_desatualizada", "Recarregue os dados do pedido."),
            StatusEntregaPropria.ChaveEmUso => Conflito("chave_em_uso", "A chave já identifica outra operação."),
            StatusEntregaPropria.ReservasInconsistentes => ReservasInconsistentes(),
            StatusEntregaPropria.LoteInelegivel => Conflito("lote_inelegivel", "Há lotes reservados inelegíveis para saída."),
            StatusEntregaPropria.SaldoInsuficiente => Conflito("saldo_insuficiente", "O saldo dos lotes é insuficiente."),
            _ => Conflito("estado_incompativel", "O estado ou a modalidade do pedido não permite esta transição."),
        };
    }

    private static IResult? LerPrecondicaoOperacional(HttpContext ctx, out Guid chave, out byte[] versao)
    {
        chave = default;
        versao = new byte[8];
        var chaves = ctx.Request.Headers["Idempotency-Key"];
        if (chaves.Count != 1 || chaves[0] is not { Length: 36 } chaveTexto ||
            !Guid.TryParseExact(chaveTexto, "D", out chave) || chave == Guid.Empty)
            return Invalido("Idempotency-Key", "Informe uma chave UUID válida.");
        var cabecalho = ctx.Request.Headers.IfMatch;
        if (cabecalho.Count != 1 || cabecalho[0] is not { Length: 14 } etag ||
            etag[0] != '"' || etag[^1] != '"' ||
            !Convert.TryFromBase64Chars(etag.AsSpan(1, 12), versao, out var bytes) || bytes != versao.Length)
            return Invalido("If-Match", "Informe a versão retornada no detalhe do pedido.");
        return null;
    }

    private static async Task<IResult> EntregaPostalAsync<T>(HttpContext ctx, Guid pedidoId,
        IEntregaPostal entrega, EtapaEntregaPostal etapa, CancellationToken ct) where T : class
    {
        if (!TentarObterAdministrador(ctx, out var administrador)) return Results.Unauthorized();
        var precondicao = LerPrecondicaoOperacional(ctx, out var chave, out var versao);
        if (precondicao is not null) return precondicao;
        var (corpo, erro) = await LerComandoAsync<T>(ctx.Request, ct);
        if (erro is not null) return erro;
        var postagem = corpo as RegistrarPostagemRequest;
        if (postagem is { Confirmado: false } || corpo is ConfirmarEntregaPostalRequest { Confirmado: false })
            return Invalido("confirmado", "Confirme a operação física antes de registrar.");
        var codigo = postagem?.CodigoRastreamento ?? (corpo as CorrigirRastreamentoPostalRequest)?.CodigoRastreamento;
        var resultado = await entrega.ExecutarAsync(new(pedidoId, administrador, chave, versao,
            etapa, postagem?.ServicoPostalUtilizado, codigo), ct);
        return resultado.Status switch
        {
            200 => Atualizado(pedidoId, resultado.Situacao!.Value, resultado.Repetida),
            403 => Results.Forbid(),
            404 => Results.NotFound(),
            400 when resultado.Codigo == "servico_postal_invalido" =>
                Invalido("servicoPostalUtilizado", "Informe o serviço postal utilizado, com até 60 caracteres."),
            400 => Invalido("codigoRastreamento", "Informe um código com duas letras, nove dígitos e duas letras finais."),
            _ => Conflito(resultado.Codigo!, "Confira a situação e a versão atualizadas do pedido."),
        };
    }

    private static async Task<IResult> ConfirmarAsync(
        HttpContext context, Guid pedidoId, ConfirmarPagamentoPedido confirmar, CancellationToken cancellationToken)
    {
        if (!TentarObterAdministrador(context, out var administradorId))
        {
            return Results.Unauthorized();
        }
        if (pedidoId == Guid.Empty)
        {
            return Invalido("pedidoId", "Informe um identificador de pedido válido.");
        }

        var (comando, erro) = await LerComandoAsync<ConfirmarPagamentoAdminRequest>(context.Request, cancellationToken)
            .ConfigureAwait(false);
        if (erro is not null)
        {
            return erro;
        }
        if (comando?.RecebimentoConferido != true)
        {
            return Invalido("recebimentoConferido", "Confirme que conferiu o recebimento no banco.");
        }

        var resultado = await confirmar.ExecuteAsync(pedidoId, administradorId, cancellationToken).ConfigureAwait(false);
        return resultado switch
        {
            ConfirmarPagamentoPedidoStatus.Confirmado or ConfirmarPagamentoPedidoStatus.Repetido =>
                Atualizado(pedidoId, SituacaoPedido.PagamentoConfirmado, resultado == ConfirmarPagamentoPedidoStatus.Repetido),
            ConfirmarPagamentoPedidoStatus.DadosInvalidos => Invalido("pedidoId", "Identificador de pedido inválido."),
            ConfirmarPagamentoPedidoStatus.AdministradorNaoAutorizado => Results.Forbid(),
            ConfirmarPagamentoPedidoStatus.PedidoNaoEncontrado => Results.NotFound(),
            ConfirmarPagamentoPedidoStatus.PrazoEncerrado => PrazoEncerrado(),
            ConfirmarPagamentoPedidoStatus.EstadoIncompativel =>
                Conflito("estado_incompativel", "A situação atual do pedido não permite confirmar o pagamento."),
            ConfirmarPagamentoPedidoStatus.ReservasInconsistentes => ReservasInconsistentes(),
            ConfirmarPagamentoPedidoStatus.ConflitoConcorrencia => ConflitoConcorrencia(),
            _ => Falha(),
        };
    }

    private static async Task<IResult> CancelarAsync(
        HttpContext context, Guid pedidoId, EncerrarPedidoCompra encerrar, CancellationToken cancellationToken)
    {
        if (!TentarObterAdministrador(context, out var administradorId))
        {
            return Results.Unauthorized();
        }
        if (pedidoId == Guid.Empty)
        {
            return Invalido("pedidoId", "Informe um identificador de pedido válido.");
        }

        var (comando, erro) = await LerComandoAsync<CancelarPedidoAdminRequest>(context.Request, cancellationToken)
            .ConfigureAwait(false);
        if (erro is not null)
        {
            return erro;
        }

        var resultado = await encerrar.CancelarAsync(pedidoId, administradorId, comando?.Motivo, cancellationToken)
            .ConfigureAwait(false);
        return resultado switch
        {
            EncerrarPedidoStatus.Cancelado or EncerrarPedidoStatus.Repetido =>
                Atualizado(pedidoId, SituacaoPedido.Cancelado, resultado == EncerrarPedidoStatus.Repetido),
            EncerrarPedidoStatus.DadosInvalidos =>
                Invalido("motivo", $"Informe um motivo de até {Pedido.MotivoCancelamentoMaxLength} caracteres."),
            EncerrarPedidoStatus.AdministradorNaoAutorizado => Results.Forbid(),
            EncerrarPedidoStatus.PedidoNaoEncontrado => Results.NotFound(),
            EncerrarPedidoStatus.PrazoEncerrado => PrazoEncerrado(),
            EncerrarPedidoStatus.EstadoIncompativel =>
                Conflito("estado_incompativel", "A situação atual do pedido não permite o cancelamento."),
            EncerrarPedidoStatus.MotivoDivergente =>
                Conflito("motivo_divergente", "O pedido já foi cancelado com outro motivo."),
            EncerrarPedidoStatus.ReservasInconsistentes => ReservasInconsistentes(),
            EncerrarPedidoStatus.ConflitoConcorrencia => ConflitoConcorrencia(),
            _ => Falha(),
        };
    }

    private static RouteHandlerBuilder ConfigurarComandoOperacional(RouteHandlerBuilder rota) =>
        rota.RequireXaquiamuniAntiforgery().Produces<ResultadoPedidoAdminResponse>()
            .ProducesValidationProblem().Produces(401).Produces(403).Produces(404)
            .ProducesProblem(409).ProducesProblem(413).ProducesProblem(415);

    private static Task<IResult> IniciarPreparacaoAsync(
        HttpContext context, Guid pedidoId, PrepararPedidoCompra preparar, CancellationToken cancellationToken) =>
        PrepararAsync(context, pedidoId, preparar.IniciarPreparacaoAsync, SituacaoPedido.EmPreparacao, cancellationToken);

    private static Task<IResult> LiberarRetiradaAsync(
        HttpContext context, Guid pedidoId, PrepararPedidoCompra preparar, CancellationToken cancellationToken) =>
        PrepararAsync(context, pedidoId, preparar.LiberarParaRetiradaAsync, SituacaoPedido.ProntoParaRetirada, cancellationToken);

    private static async Task<IResult> PrepararAsync(
        HttpContext context, Guid pedidoId, Func<Guid, Guid, CancellationToken, Task<PrepararPedidoStatus>> executar,
        SituacaoPedido destino, CancellationToken cancellationToken)
    {
        if (!TentarObterAdministrador(context, out var administradorId))
        {
            return Results.Unauthorized();
        }
        if (pedidoId == Guid.Empty)
        {
            return Invalido("pedidoId", "Informe um identificador de pedido válido.");
        }

        var (_, erro) = await LerComandoAsync<AvancarPreparacaoAdminRequest>(context.Request, cancellationToken)
            .ConfigureAwait(false);
        if (erro is not null)
        {
            return erro;
        }

        var resultado = await executar(pedidoId, administradorId, cancellationToken).ConfigureAwait(false);
        return resultado switch
        {
            PrepararPedidoStatus.Sucesso or PrepararPedidoStatus.Repetida =>
                Atualizado(pedidoId, destino, resultado == PrepararPedidoStatus.Repetida),
            PrepararPedidoStatus.DadosInvalidos => Invalido("pedidoId", "Identificador de pedido inválido."),
            PrepararPedidoStatus.AdministradorNaoAutorizado => Results.Forbid(),
            PrepararPedidoStatus.PedidoNaoEncontrado => Results.NotFound(),
            PrepararPedidoStatus.EstadoIncompativel =>
                Conflito("estado_incompativel", "A situação atual do pedido não permite essa operação."),
            PrepararPedidoStatus.ReservasInconsistentes => ReservasInconsistentes(),
            PrepararPedidoStatus.ConflitoConcorrencia => ConflitoConcorrencia(),
            _ => Falha(),
        };
    }

    private static async Task<IResult> ConfirmarEntregaAsync(
        HttpContext context, Guid pedidoId, ConfirmarEntregaPedido confirmar, CancellationToken cancellationToken)
    {
        if (!TentarObterAdministrador(context, out var administradorId))
        {
            return Results.Unauthorized();
        }
        if (pedidoId == Guid.Empty)
        {
            return Invalido("pedidoId", "Informe um identificador de pedido válido.");
        }

        var (comando, erro) = await LerComandoAsync<ConfirmarEntregaAdminRequest>(context.Request, cancellationToken)
            .ConfigureAwait(false);
        if (erro is not null)
        {
            return erro;
        }
        if (comando?.EntregaPresencialConfirmada != true)
        {
            return Invalido("entregaPresencialConfirmada", "Confirme que a entrega presencial ocorreu.");
        }

        var resultado = await confirmar.ExecuteAsync(pedidoId, administradorId, comando.EntregaPresencialConfirmada,
            cancellationToken).ConfigureAwait(false);
        return resultado.Status switch
        {
            ConfirmarEntregaPedidoStatus.Entregue or ConfirmarEntregaPedidoStatus.Repetida =>
                Atualizado(pedidoId, SituacaoPedido.Entregue, resultado.Status == ConfirmarEntregaPedidoStatus.Repetida),
            ConfirmarEntregaPedidoStatus.DadosInvalidos => Invalido("pedidoId", "Identificador de pedido inválido."),
            ConfirmarEntregaPedidoStatus.AdministradorNaoAutorizado => Results.Forbid(),
            ConfirmarEntregaPedidoStatus.PedidoNaoEncontrado => Results.NotFound(),
            ConfirmarEntregaPedidoStatus.EstadoIncompativel =>
                Conflito("estado_incompativel", "A situação atual do pedido não permite confirmar a entrega."),
            ConfirmarEntregaPedidoStatus.ReservasInconsistentes => ReservasInconsistentes(),
            ConfirmarEntregaPedidoStatus.LoteInelegivel =>
                Conflito("lote_inelegivel", "Há estoque reservado vencido ou inelegível para entrega."),
            ConfirmarEntregaPedidoStatus.SaldoInsuficiente =>
                Conflito("saldo_insuficiente", "O estoque disponível nos lotes reservados é insuficiente para a entrega."),
            ConfirmarEntregaPedidoStatus.ConflitoConcorrencia => ConflitoConcorrencia(),
            _ => Falha(),
        };
    }

    internal static async Task<(T? Comando, IResult? Erro)> LerComandoAsync<T>(
        HttpRequest request, CancellationToken cancellationToken) where T : class
    {
        if (!request.HasJsonContentType())
        {
            return (null, Results.Problem(statusCode: 415, title: "Envie um corpo JSON."));
        }
        if (request.ContentLength > MaxBytesComando)
        {
            return (null, CorpoMuitoGrande());
        }

        var buffer = new byte[MaxBytesComando + 1];
        try
        {
            var bytes = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken)
                .ConfigureAwait(false);
            if (bytes > MaxBytesComando)
            {
                return (null, CorpoMuitoGrande());
            }

            var comando = JsonSerializer.Deserialize<T>(buffer.AsSpan(0, bytes), JsonComando);
            return comando is null ? (null, CorpoInvalido()) : (comando, null);
        }
        catch (JsonException)
        {
            return (null, CorpoInvalido());
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode is 400 or 413)
        {
            return (null, exception.StatusCode == 413 ? CorpoMuitoGrande() : CorpoInvalido());
        }
    }

    private static bool TentarObterAdministrador(HttpContext context, out Guid administradorId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out administradorId)
        && administradorId != Guid.Empty;

    private static IResult Atualizado(Guid id, SituacaoPedido situacao, bool repetida) =>
        Results.Ok(new ResultadoPedidoAdminResponse(id, situacao.ToString(), repetida));

    private static IResult CorpoMuitoGrande() => Results.Problem(statusCode: 413, title: "O corpo excede 4 KiB.");
    private static IResult CorpoInvalido() => Invalido("corpo", "Envie um JSON válido com apenas os campos do comando.");
    private static IResult Falha() => Results.Problem(statusCode: 500, title: "Não foi possível concluir a solicitação.");
    private static IResult PrazoEncerrado() => Conflito("prazo_encerrado", "O prazo de pagamento terminou.");
    private static IResult ReservasInconsistentes() =>
        Conflito("reservas_inconsistentes", "O pedido não pôde ser atualizado por uma divergência de estoque.");
    private static IResult ConflitoConcorrencia() =>
        Conflito("conflito_concorrencia", "O pedido está sendo atualizado. Consulte a situação e tente novamente.");
    private static IResult Invalido(string campo, string mensagem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [mensagem] });
    private static IResult Conflito(string codigo, string titulo) =>
        Results.Problem(statusCode: 409, title: titulo,
            extensions: new Dictionary<string, object?> { ["codigo"] = codigo });
}
