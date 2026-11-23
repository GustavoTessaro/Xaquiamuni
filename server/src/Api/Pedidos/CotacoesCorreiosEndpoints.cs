using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Api.Pedidos;

public static class CotacoesCorreiosEndpoints
{
    private const string Base = "/api/cotacoes-correios";

    public static void MapCotacoesCorreiosEndpoints(this IEndpointRouteBuilder app)
    {
        var cliente = app.MapGroup(Base).RequireAuthorization(AuthorizationPolicies.ClienteAutenticado);
        var admin = app.MapGroup("/api/admin/cotacoes-correios").RequireAuthorization(AuthorizationPolicies.Administracao);
        Configurar(cliente, false);
        Configurar(admin, true);
        cliente.MapPost("", (HttpContext ctx, ICotacoesCorreios api, CancellationToken ct) =>
            EscreverAsync<SolicitacaoRequest>(ctx, api, false, null, AcaoCotacaoCorreios.Criar, ct))
            .RequireXaquiamuniAntiforgery();
        cliente.MapPost("/{id:guid}/aceitar", (HttpContext ctx, Guid id, ICotacoesCorreios api, CancellationToken ct) =>
            EscreverAsync<DecidirCotacaoCorreios>(ctx, api, false, id, AcaoCotacaoCorreios.Aceitar, ct))
            .RequireXaquiamuniAntiforgery();
        admin.MapPost("/{id:guid}/propostas/previa", (HttpContext ctx, Guid id, ICotacoesCorreios api, CancellationToken ct) =>
            EscreverAsync<PublicarPropostaCorreios>(ctx, api, true, id, AcaoCotacaoCorreios.Publicar, ct, previa: true))
            .RequireXaquiamuniAntiforgery();
        admin.MapPost("/{id:guid}/propostas", (HttpContext ctx, Guid id, ICotacoesCorreios api, CancellationToken ct) =>
            EscreverAsync<PublicarPropostaCorreios>(ctx, api, true, id, AcaoCotacaoCorreios.Publicar, ct))
            .RequireXaquiamuniAntiforgery();
    }

    private static void Configurar(RouteGroupBuilder grupo, bool admin)
    {
        grupo.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = "private, no-store";
            return await next(ctx);
        });
        grupo.MapGet("", (HttpContext ctx, ICotacoesCorreios api, int? pagina, int? itensPorPagina, string? situacao,
            DateTimeOffset? criadaDeUtc, DateTimeOffset? criadaAteUtc, CancellationToken ct) =>
            ListarAsync(ctx, api, admin, pagina ?? 1, itensPorPagina ?? 20, situacao, criadaDeUtc, criadaAteUtc, ct));
        grupo.MapGet("/{id:guid}", async Task<IResult> (HttpContext ctx, ICotacoesCorreios api, Guid id, CancellationToken ct) =>
        {
            if (!ObterAutor(ctx, out var autor)) return Results.Unauthorized();
            var detalhe = await api.BuscarAsync(admin ? null : autor, id, ct);
            if (detalhe is null) return Results.NotFound();
            DefinirVersao(ctx, detalhe.Versao);
            return Results.Ok(detalhe);
        });
        MapearDecisao("recusar", AcaoCotacaoCorreios.Recusar);
        MapearDecisao("cancelar", AcaoCotacaoCorreios.Cancelar);

        void MapearDecisao(string rota, AcaoCotacaoCorreios acao) =>
            grupo.MapPost("/{id:guid}/" + rota, (HttpContext ctx, ICotacoesCorreios api, Guid id, CancellationToken ct) =>
                EscreverAsync<DecidirCotacaoCorreios>(ctx, api, admin, id, acao, ct))
                .RequireXaquiamuniAntiforgery();
    }

    private static async Task<IResult> ListarAsync(HttpContext ctx, ICotacoesCorreios api, bool admin,
        int pagina, int tamanho, string? situacao, DateTimeOffset? de, DateTimeOffset? ate, CancellationToken ct)
    {
        if (!ObterAutor(ctx, out var autor)) return Results.Unauthorized();
        if (pagina < 1 || tamanho is < 1 or > 50 || (pagina - 1L) * tamanho > int.MaxValue ||
            (de is { Offset: var deOffset } && deOffset != TimeSpan.Zero) ||
            (ate is { Offset: var ateOffset } && ateOffset != TimeSpan.Zero) || de >= ate)
            return Erro(400, "filtro_invalido");
        SituacaoCotacaoCorreios? estado = null;
        if (situacao is not null)
        {
            if (!Enum.TryParse<SituacaoCotacaoCorreios>(situacao, true, out var valor) ||
                !string.Equals(valor.ToString(), situacao, StringComparison.OrdinalIgnoreCase) || !Enum.IsDefined(valor))
                return Erro(400, "situacao_invalida");
            estado = valor;
        }
        return Results.Ok(await api.ListarAsync(admin ? null : autor, new(pagina, tamanho, estado, de, ate), ct));
    }

    private static async Task<IResult> EscreverAsync<T>(HttpContext ctx, ICotacoesCorreios api,
        bool admin, Guid? id, AcaoCotacaoCorreios acao, CancellationToken ct, bool previa = false) where T : class
    {
        if (!ObterAutor(ctx, out var autor)) return Results.Unauthorized();
        var chaves = ctx.Request.Headers["Idempotency-Key"];
        var chave = Guid.Empty;
        if (!previa && (chaves.Count != 1 || !Guid.TryParseExact(chaves[0], "D", out chave) || chave == Guid.Empty))
            return Erro(400, "chave_invalida");
        int? versao = null;
        if (id.HasValue)
        {
            var tags = ctx.Request.Headers.IfMatch;
            if (tags.Count != 1 || tags[0] is not { Length: >= 3 } tag || tag[0] != '"' || tag[^1] != '"' ||
                !int.TryParse(tag.AsSpan(1, tag.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
                || numero <= 0)
                return Erro(400, "versao_obrigatoria");
            versao = numero;
        }

        var (corpo, erro) = id.HasValue ? await AdminPedidosEndpoints.LerComandoAsync<T>(ctx.Request, ct)
            : await PedidosClienteEndpoints.LerCompraAsync<T>(ctx.Request, ct);
        if (erro is not null) return erro;
        if (corpo is null) return Erro(400, "corpo_invalido");
        if (acao == AcaoCotacaoCorreios.Aceitar && corpo is DecidirCotacaoCorreios { Revisao: not > 0 })
            return Erro(400, "revisao_obrigatoria");
        var pedido = corpo is SolicitacaoRequest solicitacao ? new SolicitarCotacaoCorreios(
            solicitacao.Itens?.Select(x => x is null ? null! : new ItemSolicitadoCorreios(x.ProdutoId, x.ProdutoVarianteId,
                x.Quantidade)).ToArray(), solicitacao.Endereco) : null;
        var resultado = await api.ExecutarAsync(new(autor, admin, chave, id, acao, versao, pedido,
            corpo as PublicarPropostaCorreios, corpo as DecidirCotacaoCorreios, previa), ct);
        if (resultado.Cotacao is { } cotacao && resultado.Versao is { } atual)
        {
            DefinirVersao(ctx, atual);
            return resultado.Status == 201 ? Results.Created($"{Base}/{cotacao.Id:D}", cotacao) : Results.Ok(cotacao);
        }
        return Erro(resultado.Status, resultado.Codigo);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SolicitacaoRequest(ItemRequest?[]? Itens, EnderecoCotacaoEntrega? Endereco);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ItemRequest(Guid ProdutoId, Guid ProdutoVarianteId, int Quantidade);

    private static void DefinirVersao(HttpContext ctx, int versao) =>
        ctx.Response.Headers.ETag = $"\"{versao.ToString(CultureInfo.InvariantCulture)}\"";
    private static bool ObterAutor(HttpContext ctx, out Guid autor) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out autor) && autor != Guid.Empty;
    private static IResult Erro(int status, string? codigo) => Results.Problem(statusCode: status,
        extensions: codigo is null ? null : new Dictionary<string, object?> { ["codigo"] = codigo });
}
