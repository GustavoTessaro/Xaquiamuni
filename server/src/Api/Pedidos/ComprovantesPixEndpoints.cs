using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Api.Pedidos;

public static class ComprovantesPixEndpoints
{
    private const int LimiteArquivo = 5 * 1024 * 1024;
    private const int LimiteRequisicao = LimiteArquivo + 16 * 1024;

    public static void MapComprovantesPixEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var cliente = endpoints.MapGroup("/api/pedidos/{pedidoId:guid}/comprovante-pix")
            .RequireAuthorization(AuthorizationPolicies.ClienteAutenticado);
        cliente.MapPost("", EnviarAsync)
            .WithMetadata(new RequestSizeLimitAttribute(LimiteRequisicao))
            .AddEndpointFilter((context, next) =>
                string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers[AntiforgeryConfiguration.HeaderName])
                    ? ValueTask.FromResult<object?>(Results.BadRequest())
                    : next(context))
            .RequireXaquiamuniAntiforgery();
        cliente.MapGet("", (Guid pedidoId, HttpContext ctx, IComprovantesPix comprovantes, CancellationToken ct) =>
            Identificar(ctx, out var id) ? ConsultarAsync(pedidoId, id, ctx, comprovantes, ct)
                : Task.FromResult(Results.Unauthorized()));
        cliente.MapGet("/conteudo", (Guid pedidoId, HttpContext ctx, IComprovantesPix comprovantes, CancellationToken ct) =>
            Identificar(ctx, out var id) ? BaixarAsync(pedidoId, id, ctx, comprovantes, ct)
                : Task.FromResult(Results.Unauthorized()));

        var admin = endpoints.MapGroup("/api/admin/pedidos/{pedidoId:guid}/comprovante-pix")
            .RequireAuthorization(AuthorizationPolicies.Administracao);
        admin.MapGet("", (Guid pedidoId, HttpContext ctx, IComprovantesPix comprovantes, CancellationToken ct) =>
            ConsultarAsync(pedidoId, null, ctx, comprovantes, ct));
        admin.MapGet("/conteudo", (Guid pedidoId, HttpContext ctx, IComprovantesPix comprovantes, CancellationToken ct) =>
            BaixarAsync(pedidoId, null, ctx, comprovantes, ct));
    }

    private static async Task<IResult> EnviarAsync(Guid pedidoId, HttpContext ctx, IComprovantesPix comprovantes, CancellationToken ct)
    {
        if (!Identificar(ctx, out var cliente)) return Results.Unauthorized();
        ctx.Response.Headers.CacheControl = "private, no-store";
        if (!await comprovantes.PertenceAoClienteAsync(pedidoId, cliente, ct)) return Results.NotFound();

        var request = ctx.Request;
        if (!LerUuid(request, "Idempotency-Key", out var chave))
            return Erro(400, "Informe Idempotency-Key em formato UUID.");
        Guid? versao = null;
        if (request.Headers.ContainsKey("X-Comprovante-Versao"))
        {
            if (!LerUuid(request, "X-Comprovante-Versao", out var informada))
                return Erro(400, "Informe a versão atual do comprovante em formato UUID.");
            versao = informada;
        }

        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite &&
            (limite.MaxRequestBodySize is null || limite.MaxRequestBodySize > LimiteRequisicao))
            limite.MaxRequestBodySize = LimiteRequisicao;
        if (request.ContentLength > LimiteRequisicao) return MuitoGrande();
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var tipo) ||
            !string.Equals(tipo.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Erro(415, "Envie o comprovante como multipart/form-data.");
        var boundary = HeaderUtilities.RemoveQuotes(tipo.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 70)
            return Erro(400, "O formulário multipart está inválido.");

        byte[] bytes;
        string? mime;
        try
        {
            var reader = new MultipartReader(boundary, request.Body) { HeadersLengthLimit = 8192, HeadersCountLimit = 8 };
            var parte = await reader.ReadNextSectionAsync(ct);
            if (parte is null || !ContentDispositionHeaderValue.TryParse(parte.ContentDisposition, out var disposicao) ||
                !disposicao.IsFileDisposition() || HeaderUtilities.RemoveQuotes(disposicao.Name).Value != "arquivo")
                return Erro(400, "Envie exatamente um arquivo no campo arquivo.");

            using var temporario = new MemoryStream();
            var buffer = new byte[32 * 1024];
            int lidos;
            while ((lidos = await parte.Body.ReadAsync(buffer, ct)) != 0)
            {
                if (temporario.Length + lidos > LimiteArquivo) return MuitoGrande();
                temporario.Write(buffer, 0, lidos);
            }
            if (temporario.Length == 0 || await reader.ReadNextSectionAsync(ct) is not null)
                return Erro(400, "Envie exatamente um arquivo não vazio.");
            bytes = temporario.ToArray();
            var nome = HeaderUtilities.RemoveQuotes(disposicao.FileNameStar.HasValue
                ? disposicao.FileNameStar : disposicao.FileName).Value;
            mime = nome is null ? null : ValidacaoComprovantePix.TipoValidado(nome, parte.ContentType, bytes);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413) { return MuitoGrande(); }
        catch (IOException) { return Erro(400, "O formulário multipart está inválido."); }
        catch (InvalidDataException) { return Erro(400, "O formulário multipart está inválido."); }

        if (mime is null) return Erro(415, "Envie um PDF, JPG, JPEG ou PNG válido.");
        var resultado = await comprovantes.EnviarAsync(pedidoId, cliente, chave, versao, mime, bytes, ct);
        return resultado.Status switch
        {
            StatusComprovantePix.Criado => Results.Created($"/api/pedidos/{pedidoId:D}/comprovante-pix", resultado.Comprovante),
            StatusComprovantePix.Repetido => Results.Ok(resultado.Comprovante),
            StatusComprovantePix.Ausente => Results.NotFound(),
            StatusComprovantePix.Inelegivel => Erro(409, "O pedido não aceita comprovantes nesta situação ou após o prazo."),
            StatusComprovantePix.VersaoDivergente => Erro(409, "Consulte a versão atual antes de substituir o comprovante."),
            _ => Erro(409, "A chave já foi usada para um envio diferente.")
        };
    }

    private static async Task<IResult> ConsultarAsync(Guid pedido, Guid? cliente, HttpContext ctx,
        IComprovantesPix comprovantes, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "private, no-store";
        var dados = await comprovantes.ConsultarAsync(pedido, cliente, ct);
        return dados is null ? Results.NotFound() : Results.Ok(dados);
    }

    private static async Task<IResult> BaixarAsync(Guid pedido, Guid? cliente, HttpContext ctx,
        IComprovantesPix comprovantes, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "private, no-store";
        ctx.Response.Headers.XContentTypeOptions = "nosniff";
        var dados = await comprovantes.BaixarAsync(pedido, cliente, ct);
        if (dados is null) return Results.NotFound();
        var extensao = dados.Tipo switch { "application/pdf" => "pdf", "image/png" => "png", _ => "jpg" };
        return Results.File(dados.Bytes, dados.Tipo, $"comprovante-pix.{extensao}");
    }

    private static bool Identificar(HttpContext ctx, out Guid id) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;

    private static bool LerUuid(HttpRequest request, string nome, out Guid id)
    {
        var valores = request.Headers[nome];
        id = Guid.Empty;
        return valores.Count == 1 && Guid.TryParseExact(valores[0], "D", out id) && id != Guid.Empty;
    }

    private static IResult MuitoGrande() => Erro(413, "O comprovante deve ter até 5 MiB.");

    private static IResult Erro(int status, string mensagem) => Results.Problem(statusCode: status, detail: mensagem);
}
