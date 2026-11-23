using System.Security.Claims;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Api.Pedidos;

public static class PedidosClienteEndpoints
{
    public const string BasePath = "/api/pedidos";
    private const int MaxItensCompra = 100;
    private const int MaxBytesCompra = 24 * 1024;
    private const int TamanhoPaginaPadrao = 20;
    private const int TamanhoPaginaMaximo = 50;
    private const string IdempotencyHeader = "Idempotency-Key";
    private static readonly JsonSerializerOptions JsonCompra = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 8,
        AllowDuplicateProperties = false,
    };

    public static IEndpointRouteBuilder MapPedidosClienteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/checkout/cotacoes-entrega", CotarEntregaAsync)
            .RequireAuthorization(AuthorizationPolicies.ClienteAutenticado)
            .RequireXaquiamuniAntiforgery().WithTags("Pedidos do cliente");
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization(AuthorizationPolicies.ClienteAutenticado)
            .WithTags("Pedidos do cliente");

        group.MapPost("", CriarAsync).WithName("CriarPedidoCliente")
            .RequireXaquiamuniAntiforgery()
            .Produces<PedidoClienteResponse>(201).Produces<PedidoClienteResponse>(200)
            .ProducesValidationProblem().Produces(401).ProducesProblem(403).ProducesProblem(409)
            .ProducesProblem(413).ProducesProblem(415).ProducesProblem(500);
        group.MapGet("", ListarAsync).WithName("ListarPedidosCliente")
            .Produces<PedidoClientePaginaResponse>().ProducesValidationProblem()
            .Produces(401).Produces(403);
        group.MapGet("/{pedidoId:guid}", BuscarAsync).WithName("BuscarPedidoCliente")
            .Produces<PedidoClienteResponse>().Produces(401).Produces(403).Produces(404);

        return endpoints;
    }

    private static async Task<IResult> CriarAsync(
        HttpContext context, CriarPedidoCompra criarPedido, CancellationToken cancellationToken)
    {
        if (!TentarObterCliente(context, out var clienteId))
        {
            return Results.Unauthorized();
        }

        var chaves = context.Request.Headers[IdempotencyHeader];
        if (chaves.Count != 1 || chaves[0] is not { Length: 36 } valor
            || !Guid.TryParseExact(valor, "D", out var chave) || chave == Guid.Empty)
        {
            return Invalido(IdempotencyHeader, "Informe uma chave UUID válida.");
        }

        var (request, erro) = await LerCompraAsync<CriarPedidoClienteRequest>(context.Request, cancellationToken);
        if (erro is not null)
            return erro;
        var itens = NormalizarItens(request?.Itens, out var precoInvalido);
        if (precoInvalido)
            return Invalido("precoAceitoCentavos", "Informe um preço positivo em centavos.");
        if (itens is null)
            return Invalido("itens", $"Informe de 1 a {MaxItensCompra} itens com quantidades e preços válidos.");
        if (request!.OfertaEntregaId is { Length: 0 or > 16000 })
            return Conflito("oferta_entrega_invalida", "Solicite uma nova cotação de entrega.");

        var input = new CriarPedidoCompraInput(clienteId, chave, itens, request.OfertaEntregaId);
        var resultado = await criarPedido.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        return ResponderCriacao(resultado);
    }

    private static async Task<IResult> CotarEntregaAsync(
        HttpContext context, ICotarEntrega cotacao, CancellationToken cancellationToken)
    {
        if (!TentarObterCliente(context, out var clienteId))
            return Results.Unauthorized();

        context.Response.Headers.CacheControl = "private, no-store";
        var (request, erro) = await LerCompraAsync<CotarEntregaRequest>(context.Request, cancellationToken);
        if (erro is not null)
            return erro;
        var itens = NormalizarItens(request?.Itens, out var precoInvalido);
        if (precoInvalido)
            return Invalido("precoAceitoCentavos", "Informe um preço positivo em centavos.");
        if (itens is null)
            return Invalido("itens", $"Informe de 1 a {MaxItensCompra} itens válidos.");
        if (request!.EnderecoCadastradoId == Guid.Empty ||
            (request.EnderecoCadastradoId.HasValue && request.Endereco is not null) ||
            (request.UsarEnderecoPadrao && (request.EnderecoCadastradoId.HasValue || request.Endereco is not null)))
            return Invalido("endereco", "Escolha um endereço cadastrado ou informe um endereço para este pedido.");

        return Results.Ok(await cotacao.CotarAsync(clienteId, itens, request.EnderecoCadastradoId,
            request.Endereco, request.UsarEnderecoPadrao, cancellationToken));
    }

    internal static async Task<(T? Request, IResult? Erro)> LerCompraAsync<T>(
        HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return (default, Results.Problem(statusCode: 415, title: "Envie um corpo JSON."));
        }

        if (request.ContentLength > MaxBytesCompra)
        {
            return (default, CorpoMuitoGrande());
        }

        // Aplica o limite também quando a solicitação chega sem Content-Length.
        var buffer = new byte[MaxBytesCompra + 1];
        int bytes;
        try
        {
            bytes = await request.Body.ReadAtLeastAsync(
                buffer, buffer.Length, false, cancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode is 400 or 413)
        {
            return (default, exception.StatusCode == 413
                ? CorpoMuitoGrande()
                : Invalido("corpo", "O corpo do pedido está incompleto."));
        }

        if (bytes > MaxBytesCompra)
        {
            return (default, CorpoMuitoGrande());
        }

        try
        {
            return (JsonSerializer.Deserialize<T>(buffer.AsSpan(0, bytes), JsonCompra), null);
        }
        catch (JsonException)
        {
            return (default, Invalido("corpo", "O corpo do pedido possui um formato inválido."));
        }
    }

    private static ItemCompraInput[]? NormalizarItens(ItemPedidoClienteRequest?[]? itens, out bool precoInvalido)
    {
        precoInvalido = false;
        if (itens is not { Length: > 0 and <= MaxItensCompra }
            || itens.Any(item => item is null))
        {
            return null;
        }

        var itensCompra = new List<ItemCompraInput>(itens.Length);
        foreach (var item in itens)
        {
            long? aceito = null;
            if (item!.PrecoAceitoCentavos is { } texto)
            {
                if (!long.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var centavos) || centavos <= 0)
                {
                    precoInvalido = true;
                    return null;
                }
                aceito = centavos;
            }
            itensCompra.Add(new(item.ProdutoId, item.ProdutoVarianteId, item.Quantidade, aceito));
        }
        return CriarPedidoCompra.NormalizarItens(itensCompra);
    }

    private static IResult ResponderCriacao(CriarPedidoCompraResult resultado) =>
        resultado.Status switch
        {
            CriarPedidoCompraStatus.Sucesso when resultado.Pedido is { } pedido =>
                resultado.Repetida ? Results.Ok(PedidoClienteResponse.From(pedido))
                    : Results.Created($"{BasePath}/{pedido.Id:D}", PedidoClienteResponse.From(pedido)),
            CriarPedidoCompraStatus.DadosInvalidos => Invalido("itens", "Verifique os produtos, variantes e quantidades."),
            CriarPedidoCompraStatus.LimiteMonetarioExcedido => Invalido("itens", "O valor do pedido excede o limite suportado."),
            CriarPedidoCompraStatus.ClienteInapto =>
                Results.Problem(statusCode: 403, title: "Cadastro inapto para realizar pedidos."),
            CriarPedidoCompraStatus.ItemNaoCompravel or CriarPedidoCompraStatus.SaldoInsuficiente =>
                Conflito("itens_indisponiveis", "Um ou mais itens do pedido estão indisponíveis."),
            CriarPedidoCompraStatus.ConflitoIdempotencia =>
                Conflito("chave_em_uso", "Esta chave já foi usada para outro conteúdo."),
            CriarPedidoCompraStatus.PrecoAlterado =>
                Results.Problem(statusCode: 409, title: "Revise os preços atuais antes de confirmar a compra.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["codigo"] = "preco_alterado",
                        ["precosAtuais"] = resultado.PrecosAtuais
                    }),
            CriarPedidoCompraStatus.ConflitoConcorrencia =>
                Conflito("conflito_concorrencia", "Tente novamente com a mesma chave."),
            CriarPedidoCompraStatus.OfertaEntregaInvalida =>
                Conflito("oferta_entrega_invalida", "Solicite uma nova cotação e confirme o frete antes de continuar."),
            _ => Results.Problem(statusCode: 500, title: "Não foi possível concluir a solicitação."),
        };

    private static async Task<IResult> ListarAsync(
        HttpContext context, int? pagina, int? itensPorPagina,
        IConsultarPedidosCliente consulta, CancellationToken cancellationToken)
    {
        if (!TentarObterCliente(context, out var clienteId))
        {
            return Results.Unauthorized();
        }

        var numero = pagina ?? 1;
        var tamanho = itensPorPagina ?? TamanhoPaginaPadrao;
        if (numero < 1 || tamanho is < 1 or > TamanhoPaginaMaximo
            || (numero - 1L) * tamanho > int.MaxValue)
        {
            return Invalido("consulta", $"Informe uma página positiva e entre 1 e {TamanhoPaginaMaximo} itens por página.");
        }

        var resultado = await consulta.ListarAsync(clienteId, (numero - 1) * tamanho, tamanho, cancellationToken)
            .ConfigureAwait(false);
        var itens = resultado.Itens.Select(pedido => new PedidoClienteResumoResponse(
            pedido.Id, pedido.Situacao.ToString(), pedido.CriadoEmUtc,
            pedido.PrazoPagamentoEmUtc, pedido.TotalCentavos)).ToArray();
        var totalPaginas = (int)((resultado.TotalItens + tamanho - 1L) / tamanho);
        return Results.Ok(new PedidoClientePaginaResponse(itens, numero, tamanho, resultado.TotalItens, totalPaginas));
    }

    private static async Task<IResult> BuscarAsync(
        HttpContext context, Guid pedidoId, IConsultarPedidosCliente consulta, CancellationToken cancellationToken)
    {
        if (!TentarObterCliente(context, out var clienteId))
        {
            return Results.Unauthorized();
        }

        var pedido = await consulta.BuscarAsync(clienteId, pedidoId, cancellationToken).ConfigureAwait(false);
        return pedido is null ? Results.NotFound() : Results.Ok(PedidoClienteResponse.From(pedido));
    }

    private static bool TentarObterCliente(HttpContext context, out Guid clienteId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out clienteId) && clienteId != Guid.Empty;

    private static IResult Invalido(string campo, string mensagem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [mensagem] });

    private static IResult CorpoMuitoGrande() =>
        Results.Problem(statusCode: 413, title: $"O corpo do pedido excede {MaxBytesCompra / 1024} KiB.");

    private static IResult Conflito(string codigo, string titulo) =>
        Results.Problem(statusCode: 409, title: titulo,
            extensions: new Dictionary<string, object?> { ["codigo"] = codigo });
}
