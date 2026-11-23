using System.Globalization;
using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Api.Catalogo;

public static class AdminPromocoesFreteEndpoints
{
    private const string BasePath = "/api/admin/promocoes-frete";

    public static IEndpointRouteBuilder MapAdminPromocoesFreteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup(BasePath).RequireAuthorization(AuthorizationPolicies.Administracao);
        grupo.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = "private, no-store";
            return await next(ctx);
        });
        grupo.MapGet("", ListarAsync);
        grupo.MapGet("/{id:guid}", ConsultarAsync);
        grupo.MapPost("", CriarAsync).RequireXaquiamuniAntiforgery();
        grupo.MapPut("/{id:guid}", EditarAsync).RequireXaquiamuniAntiforgery();
        grupo.MapPatch("/{id:guid}/status", AlterarStatusAsync).RequireXaquiamuniAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ListarAsync(int? pagina, EfPromocoesFrete repo, CancellationToken ct)
    {
        if (pagina is < 1 or > 1_000_000) return Invalido("pagina", "Informe uma página válida.");
        var campanhas = await repo.ListarAsync(pagina ?? 1, ct);
        return Results.Ok(new
        {
            itens = campanhas.Take(EfPromocoesFrete.TamanhoPagina).Select(Mapear).ToArray(),
            temProxima = campanhas.Count > EfPromocoesFrete.TamanhoPagina
        });
    }

    private static async Task<IResult> ConsultarAsync(Guid id, HttpContext ctx, EfPromocoesFrete repo, CancellationToken ct) =>
        await repo.ConsultarAsync(id, ct) is { } campanha ? Responder(ctx, campanha) : NaoEncontrada();

    private static Task<IResult> CriarAsync(HttpContext ctx, EfPromocoesFrete repo, CancellationToken ct) =>
        SalvarConfiguracaoAsync(null, ctx, repo, ct);

    private static Task<IResult> EditarAsync(Guid id, HttpContext ctx, EfPromocoesFrete repo, CancellationToken ct) =>
        SalvarConfiguracaoAsync(id, ctx, repo, ct);

    private static async Task<IResult> SalvarConfiguracaoAsync(Guid? id, HttpContext ctx, EfPromocoesFrete repo, CancellationToken ct)
    {
        if (!TentarAutor(ctx, out var autor)) return Results.Unauthorized();
        var versao = 0;
        if (id.HasValue && !TentarVersao(ctx, out versao)) return VersaoAusente();
        var (dados, erro) = await AdminPedidosEndpoints.LerComandoAsync<ConfiguracaoRequest>(ctx.Request, ct);
        if (erro is not null) return erro;
        var tipo = dados!.Tipo switch
        {
            "Percentual" => TipoDescontoFrete.Percentual,
            "ValorFixo" => TipoDescontoFrete.ValorFixo,
            "FreteGratis" => TipoDescontoFrete.FreteGratis,
            _ => (TipoDescontoFrete)0
        };
        if (tipo == 0) return Invalido("tipo", "Escolha o tipo de desconto.");

        if (!long.TryParse(dados.Valor, NumberStyles.None, CultureInfo.InvariantCulture, out var valor))
            return Invalido("valor", "Informe o valor em centavos ou pontos-base, conforme o tipo de desconto.");
        if (!long.TryParse(dados.SubtotalMinimoCentavos, NumberStyles.None, CultureInfo.InvariantCulture, out var subtotal))
            return Invalido("subtotalMinimoCentavos", "Informe o subtotal mínimo em centavos.");

        var modalidades = (ModalidadesPromocaoFrete)0;
        foreach (var nome in dados.Modalidades ?? [])
        {
            var modalidade = nome switch
            {
                "EntregaPropria" => ModalidadesPromocaoFrete.EntregaPropria,
                "CorreiosManual" => ModalidadesPromocaoFrete.CorreiosManual,
                _ => (ModalidadesPromocaoFrete)0
            };
            if (modalidade == 0 || (modalidades & modalidade) != 0)
                return Invalido("modalidades", "Selecione modalidades distintas entre entrega própria e Correios manual.");
            modalidades |= modalidade;
        }

        var configuracao = new ConfiguracaoPromocaoFrete(dados.Nome ?? "", tipo, valor,
            modalidades, subtotal, dados.InicioUtc, dados.FimUtc);
        try
        {
            return id is { } existente
                ? ResponderAlteracao(ctx, await repo.AlterarAsync(existente, versao, configuracao, null, autor, ct))
                : Responder(ctx, await repo.CriarAsync(configuracao, autor, ct), criada: true);
        }
        catch (ArgumentException e) when (e.ParamName is "nome" or "valor" or "modalidades" or "subtotalMinimoCentavos" or "fimUtc")
        {
            return Invalido(e.ParamName, "Revise este campo e os limites informados no formulário.");
        }
    }

    private static async Task<IResult> AlterarStatusAsync(Guid id, HttpContext ctx, EfPromocoesFrete repo, CancellationToken ct)
    {
        if (!TentarAutor(ctx, out var autor)) return Results.Unauthorized();
        if (!TentarVersao(ctx, out var versao)) return VersaoAusente();
        var (dados, erro) = await AdminPedidosEndpoints.LerComandoAsync<StatusRequest>(ctx.Request, ct);
        if (erro is not null) return erro;
        return dados?.Ativa is { } ativa
            ? ResponderAlteracao(ctx, await repo.AlterarAsync(id, versao, null, ativa, autor, ct))
            : Invalido("ativa", "Informe se a campanha deve ficar ativa.");
    }

    private static bool TentarAutor(HttpContext ctx, out Guid autor) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out autor) && autor != Guid.Empty;

    private static bool TentarVersao(HttpContext ctx, out int versao)
    {
        versao = 0;
        var campo = ctx.Request.Headers.IfMatch.ToString();
        return campo is ['"', .., '"'] && int.TryParse(campo.AsSpan(1, campo.Length - 2),
            NumberStyles.None, CultureInfo.InvariantCulture, out versao) && versao > 0;
    }

    private static IResult ResponderAlteracao(HttpContext ctx, (PromocaoFrete? Campanha, bool Conflito) resultado) =>
        resultado.Conflito
            ? Results.Problem(statusCode: 409, detail: "A campanha mudou. Recarregue os dados antes de editar.")
            : resultado.Campanha is { } campanha ? Responder(ctx, campanha) : NaoEncontrada();

    private static IResult Responder(HttpContext ctx, PromocaoFrete campanha, bool criada = false)
    {
        ctx.Response.Headers.ETag = $"\"{campanha.Versao}\"";
        return criada ? Results.Created($"{BasePath}/{campanha.Id:D}", Mapear(campanha)) : Results.Ok(Mapear(campanha));
    }

    private static DetalheCampanha Mapear(PromocaoFrete c) => new(c.Id, c.Nome, c.Tipo.ToString(),
        c.Valor.ToString(CultureInfo.InvariantCulture),
        Enum.GetValues<ModalidadesPromocaoFrete>().Where(m => (c.Modalidades & m) != 0).Select(m => m.ToString()).ToArray(),
        c.SubtotalMinimoCentavos.ToString(CultureInfo.InvariantCulture), c.InicioUtc, c.FimUtc, c.Ativa, c.Versao,
        c.CriadaPorId, c.CriadaEmUtc, c.AlteradaPorId, c.AlteradaEmUtc);

    private static IResult NaoEncontrada() => Results.Problem(statusCode: 404, detail: "Campanha de frete não encontrada.");
    private static IResult VersaoAusente() => Results.Problem(statusCode: 428, detail: "Informe a versão atual no If-Match.");
    private static IResult Invalido(string campo, string detalhe) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [detalhe] });

    private sealed record ConfiguracaoRequest(string? Nome, string? Tipo, string? Valor, string[]? Modalidades,
        string? SubtotalMinimoCentavos, DateTimeOffset? InicioUtc, DateTimeOffset? FimUtc);
    private sealed record StatusRequest(bool? Ativa);
    private sealed record DetalheCampanha(Guid Id, string Nome, string Tipo, string Valor, string[] Modalidades,
        string SubtotalMinimoCentavos, DateTimeOffset? InicioUtc, DateTimeOffset? FimUtc, bool Ativa,
        int Versao, Guid CriadaPorId, DateTimeOffset CriadaEmUtc, Guid AlteradaPorId, DateTimeOffset AlteradaEmUtc);
}
