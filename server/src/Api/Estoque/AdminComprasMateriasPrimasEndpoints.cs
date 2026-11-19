using System.Globalization;
using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Api.Estoque;

public static class AdminComprasMateriasPrimasEndpoints
{
    private const string Rota = "/api/admin/compras-materias-primas";

    public static void MapAdminComprasMateriasPrimasEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Rota).RequireAuthorization(AuthorizationPolicies.Administracao);
        group.MapGet("", ListarAsync);
        group.MapGet("/{id:guid}", ConsultarAsync);
        group.MapPost("", CriarAsync).RequireXaquiamuniAntiforgery();
        group.MapPut("/{id:guid}", EditarAsync).RequireXaquiamuniAntiforgery();
        group.MapPost("/{id:guid}/cancelar", CancelarAsync).RequireXaquiamuniAntiforgery();
        group.MapPost("/{id:guid}/receber", ReceberAsync).RequireXaquiamuniAntiforgery();
    }

    private static async Task<IResult> ListarAsync(string? busca, string? situacao,
        IGerenciarComprasMateriaPrima compras, CancellationToken ct)
    {
        SituacaoCompraMateriaPrima? filtro = null;
        if (situacao is not null)
        {
            if (!Enum.TryParse<SituacaoCompraMateriaPrima>(situacao, out var valor) || valor.ToString() != situacao)
                return Invalida("Informe Rascunho, Cancelada ou Recebida como situação.");
            filtro = valor;
        }
        if (busca?.Length > 120) return Invalida("A busca deve ter até 120 caracteres.");
        var lista = await compras.ListarAsync(busca, filtro, ct);
        return Results.Ok(lista.Select(RespostaResumo));
    }

    private static async Task<IResult> ConsultarAsync(Guid id, IGerenciarComprasMateriaPrima compras,
        CancellationToken ct) =>
        await compras.ConsultarAsync(id, ct) is { } compra ? Results.Ok(RespostaDetalhe(compra)) : Results.NotFound();

    private static async Task<IResult> CriarAsync(CompraMateriaPrimaInput input, HttpContext context,
        IGerenciarComprasMateriaPrima compras, CancellationToken ct)
    {
        if (!Autor(context, out var autor)) return Results.Forbid();
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var chave) || chave == Guid.Empty)
            return Invalida("Informe uma chave de idempotência válida.");
        return Mapear(await compras.CriarAsync(input, autor, chave, ct), criar: true);
    }

    private static async Task<IResult> EditarAsync(Guid id, CompraMateriaPrimaInput input, HttpContext context,
        IGerenciarComprasMateriaPrima compras, CancellationToken ct)
    {
        if (!Autor(context, out var autor)) return Results.Forbid();
        if (input.Versao is null || input.Versao == Guid.Empty) return Invalida("Informe a versão do rascunho.");
        return Mapear(await compras.EditarAsync(id, input, autor, ct));
    }

    private static async Task<IResult> CancelarAsync(Guid id, CancelarCompraRequest input, HttpContext context,
        IGerenciarComprasMateriaPrima compras, CancellationToken ct)
    {
        if (!Autor(context, out var autor)) return Results.Forbid();
        if (input.Versao == Guid.Empty) return Invalida("Informe a versão do rascunho.");
        return Mapear(await compras.CancelarAsync(id, input.Versao, autor, ct));
    }

    private static async Task<IResult> ReceberAsync(Guid id, ReceberCompraMateriaPrimaInput input, HttpContext context,
        IReceberCompraMateriaPrima recebimento, CancellationToken ct)
    {
        if (!Autor(context, out var autor)) return Results.Forbid();
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var chave) || chave == Guid.Empty)
            return Invalida("Informe uma chave de idempotência válida.");

        var resultado = await recebimento.ReceberAsync(id, autor, chave, input, ct);
        return resultado.Status switch
        {
            CompraMateriaPrimaStatus.Sucesso or CompraMateriaPrimaStatus.Repetida => Results.Ok(new
            {
                compraId = resultado.Compra!.Id,
                situacao = resultado.Compra.Situacao.ToString(),
                versao = resultado.Compra.Versao,
                recebidaEmUtc = resultado.Compra.RecebidaEmUtc,
                recebidaPorId = resultado.Compra.RecebidaPorId,
                entradas = resultado.Entradas!.Select(e => new { e.LinhaId, e.ComponenteId, e.MovimentoId })
            }),
            CompraMateriaPrimaStatus.Ausente => Results.NotFound(),
            CompraMateriaPrimaStatus.Conflito => Results.Problem(statusCode: 409, detail: resultado.Erro),
            _ => Invalida(resultado.Erro ?? "Confira os dados do recebimento.")
        };
    }

    private static IResult Mapear(CompraMateriaPrimaResult result, bool criar = false) => result.Status switch
    {
        CompraMateriaPrimaStatus.Sucesso when criar => Results.Created($"{Rota}/{result.Compra!.Id}", RespostaDetalhe(result.Compra)),
        CompraMateriaPrimaStatus.Sucesso or CompraMateriaPrimaStatus.Repetida => Results.Ok(RespostaDetalhe(result.Compra!)),
        CompraMateriaPrimaStatus.Ausente => Results.NotFound(),
        CompraMateriaPrimaStatus.Conflito => Results.Problem(statusCode: 409, detail: result.Erro),
        _ => Invalida(result.Erro ?? "Confira os dados do rascunho.")
    };

    private static IResult Invalida(string erro) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["compra"] = [erro] });

    private static bool Autor(HttpContext context, out Guid id) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;

    private static ResumoCompra RespostaResumo(CompraMateriaPrima c) =>
        new(c.Id, c.FornecedorId, c.NomeFornecedor, c.DataCompra, c.Situacao.ToString(), DecimalTexto(c.Total));
    private static DetalheCompra RespostaDetalhe(CompraMateriaPrima c) =>
        new(RespostaResumo(c), c.Versao, c.CriadoEmUtc, c.AtualizadoEmUtc, c.CanceladoEmUtc, c.RecebidaEmUtc, c.RecebidaPorId,
            c.Linhas.OrderBy(l => l.Ordem).Select(l => new LinhaCompra(l.Kit, DecimalTexto(l.Quantidade)!,
                DecimalTexto(l.ValorTotal), l.Componentes.OrderBy(i => i.Ordem).Select(i => new ComponenteCompra(
                    i.Id, i.InsumoId, i.NomeInsumo, i.ApresentacaoId, i.NomeApresentacao, DecimalTexto(i.Quantidade)!,
                    DecimalTexto(i.Fator), DecimalTexto(i.QuantidadeBase)!, i.UnidadeBase.ToString())).ToArray())).ToArray());

    private static string? DecimalTexto(decimal? valor) => valor?.ToString("0.######", CultureInfo.InvariantCulture);

    public sealed record CancelarCompraRequest(Guid Versao);
    public sealed record ResumoCompra(Guid Id, Guid FornecedorId, string NomeFornecedor, DateOnly DataCompra,
        string Situacao, string? Total);
    public sealed record DetalheCompra(ResumoCompra Resumo, Guid Versao, DateTimeOffset CriadoEmUtc,
        DateTimeOffset AtualizadoEmUtc, DateTimeOffset? CanceladoEmUtc, DateTimeOffset? RecebidaEmUtc,
        Guid? RecebidaPorId, IReadOnlyList<LinhaCompra> Linhas);
    public sealed record LinhaCompra(string? Kit, string Quantidade, string? ValorTotal, IReadOnlyList<ComponenteCompra> Componentes);
    public sealed record ComponenteCompra(Guid Id, Guid InsumoId, string NomeInsumo, Guid? ApresentacaoId,
        string? NomeApresentacao, string Quantidade, string? Fator, string QuantidadeBase, string UnidadeBase);
}
