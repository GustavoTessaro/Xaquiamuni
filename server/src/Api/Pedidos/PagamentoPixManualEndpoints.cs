using System.Security.Claims;
using System.Text.Json.Serialization;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Pedidos;

public static class PagamentoPixManualEndpoints
{
    public const string AdminPath = "/api/admin/configuracoes/pix-manual";

    public static IEndpointRouteBuilder MapPagamentoPixManualEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup(AdminPath).RequireAuthorization(AuthorizationPolicies.Administracao);
        admin.MapGet("", ConsultarAsync);
        admin.MapPut("", SalvarAsync).RequireXaquiamuniAntiforgery();
        endpoints.MapGet("/api/pedidos/{pedidoId:guid}/pagamento", ConsultarPedidoAsync)
            .RequireAuthorization(AuthorizationPolicies.ClienteAutenticado);
        return endpoints;
    }

    private static async Task<IResult> ConsultarAsync(
        HttpContext context, IPagamentoPixManual pix, CancellationToken cancellationToken)
    {
        if (!ObterUsuario(context, out _)) return Results.Unauthorized();
        context.Response.Headers.CacheControl = "no-store";
        var estado = await pix.ConsultarConfiguracaoAsync(cancellationToken);
        return Results.Ok(ConfiguracaoPixResponse.From(estado));
    }

    private static async Task<IResult> SalvarAsync(
        HttpContext context, IPagamentoPixManual pix, CancellationToken cancellationToken)
    {
        if (!ObterUsuario(context, out var autorId)) return Results.Unauthorized();
        var (comando, erro) = await AdminPedidosEndpoints.LerComandoAsync<SalvarPixManualRequest>(context.Request, cancellationToken);
        if (erro is not null) return erro;
        if (!Enum.TryParse<TipoChavePixManual>(comando!.TipoChave, ignoreCase: true, out var tipo)
            || !string.Equals(tipo.ToString(), comando.TipoChave, StringComparison.OrdinalIgnoreCase))
        {
            return Invalido("tipoChave", "Selecione Aleatoria, Email, Telefone ou Cnpj.");
        }

        ConfiguracaoPixManual configuracao;
        try
        {
            configuracao = ConfiguracaoPixManual.Criar(tipo, comando.ChavePix, comando.NomeRecebedor, comando.InstrucoesAdicionais);
        }
        catch (ArgumentException exception)
        {
            var campo = exception.ParamName ?? "configuracao";
            return Invalido(campo, exception.Message.Split(" (Parameter", 2)[0]);
        }

        if (comando.Revisao is not >= 0)
            return Invalido("revisao", "Informe a revisão da configuração.");
        if (!Guid.TryParseExact(context.Request.Headers["Idempotency-Key"].ToString(), "D", out var chave)
            || chave == Guid.Empty)
            return Invalido("idempotencyKey", "Informe uma chave de idempotência válida.");

        context.Response.Headers.CacheControl = "no-store";
        var resultado = await pix.SalvarAsync(configuracao, comando.Revisao.Value, autorId,
            chave, comando.ConfirmarPendentes, cancellationToken);
        return resultado.Status switch
        {
            StatusSalvarPix.Sucesso => Results.Ok(ConfiguracaoPixResponse.From(resultado.Estado!)),
            StatusSalvarPix.ConfirmacaoPendente => Results.Conflict(new { codigo = "pix_confirmacao_pendente" }),
            StatusSalvarPix.RevisaoDesatualizada => Results.Conflict(new { codigo = "pix_revisao_desatualizada" }),
            _ => Results.Conflict(new { codigo = "pix_chave_incompativel" }),
        };
    }

    private static async Task<IResult> ConsultarPedidoAsync(
        HttpContext context, Guid pedidoId, IPagamentoPixManual pix, CancellationToken cancellationToken)
    {
        if (!ObterUsuario(context, out var clienteId)) return Results.Unauthorized();
        var consulta = await pix.ConsultarPedidoAsync(pedidoId, clienteId, cancellationToken);
        if (consulta is null) return Results.NotFound();

        context.Response.Headers.CacheControl = "no-store";
        var situacao = !consulta.PagamentoPermitido ? "pagamento_indisponivel"
            : consulta.Dados is null ? "configuracao_ausente" : "disponivel";
        return Results.Ok(new InstrucoesPixPedidoResponse(situacao, consulta.ValorCentavos,
            consulta.PrazoPagamentoEmUtc, consulta.PagamentoPermitido && consulta.Dados is { } config
                ? DadosPixResponse.From(config) : null, consulta.Legado));
    }

    private static bool ObterUsuario(HttpContext context, out Guid id) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;

    private static IResult Invalido(string campo, string mensagem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [mensagem] });
}

public sealed record SalvarPixManualRequest(string? TipoChave, string? ChavePix,
    string? NomeRecebedor, string? InstrucoesAdicionais, long? Revisao = null, bool ConfirmarPendentes = false);

public sealed record DadosPixResponse(string TipoChave, string ChavePix, string NomeRecebedor, string? InstrucoesAdicionais)
{
    internal static DadosPixResponse From(DadosPixPedido configuracao) =>
        new(configuracao.TipoChave.ToString(), configuracao.ChavePix, configuracao.NomeRecebedor, configuracao.InstrucoesAdicionais);
}

public sealed record ConfiguracaoPixResponse(DadosPixResponse? Pix, long Revisao, int PedidosPendentes)
{
    internal static ConfiguracaoPixResponse From(EstadoConfiguracaoPix estado) =>
        new(estado.Dados is { } dados ? DadosPixResponse.From(dados) : null, estado.Revisao, estado.PedidosPendentes);
}

public sealed record InstrucoesPixPedidoResponse(string Situacao,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long ValorCentavos,
    DateTimeOffset PrazoPagamentoEmUtc, DadosPixResponse? Pix, bool Legado);
