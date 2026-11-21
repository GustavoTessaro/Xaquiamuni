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

        if (!context.Request.HasJsonContentType())
        {
            return Results.Problem(statusCode: 415, title: "Envie um corpo JSON.");
        }

        if (context.Request.ContentLength > MaxBytesCompra)
        {
            return CorpoMuitoGrande();
        }

        // Aplica o limite também quando a solicitação chega sem Content-Length.
        var buffer = new byte[MaxBytesCompra + 1];
        int bytes;
        try
        {
            bytes = await context.Request.Body.ReadAtLeastAsync(
                buffer, buffer.Length, false, cancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode is 400 or 413)
        {
            return exception.StatusCode == 413
                ? CorpoMuitoGrande()
                : Invalido("corpo", "O corpo do pedido está incompleto.");
        }

        if (bytes > MaxBytesCompra)
        {
            return CorpoMuitoGrande();
        }

        CriarPedidoClienteRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CriarPedidoClienteRequest>(buffer.AsSpan(0, bytes), JsonCompra);
        }
        catch (JsonException)
        {
            return Invalido("corpo", "O corpo do pedido possui um formato inválido.");
        }

        if (request?.Itens is not { Length: > 0 and <= MaxItensCompra } itens
            || itens.Any(item => item is null))
        {
            return Invalido("itens", $"Informe de 1 a {MaxItensCompra} itens válidos.");
        }

        var itensCompra = new List<ItemCompraInput>(itens.Length);
        foreach (var item in itens)
        {
            long? aceito = null;
            if (item!.PrecoAceitoCentavos is { } texto)
            {
                if (!long.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var centavos) || centavos <= 0)
                    return Invalido("precoAceitoCentavos", "Informe um preço positivo em centavos.");
                aceito = centavos;
            }
            itensCompra.Add(new(item.ProdutoId, item.ProdutoVarianteId, item.Quantidade, aceito));
        }
        var input = new CriarPedidoCompraInput(clienteId, chave, itensCompra);
        var resultado = await criarPedido.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        return resultado.Status switch
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
            _ => Results.Problem(statusCode: 500, title: "Não foi possível concluir a solicitação."),
        };
    }

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
