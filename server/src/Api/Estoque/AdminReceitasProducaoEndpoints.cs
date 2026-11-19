using System.Globalization;
using System.Security.Claims;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Api.Estoque;

public static class AdminReceitasProducaoEndpoints
{
    private const string Rota = "/api/admin/receitas";

    public static void MapAdminReceitasProducaoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Rota).RequireAuthorization(AuthorizationPolicies.Administracao);
        group.MapGet("", async (IGerenciarReceitasProducao receitas, CancellationToken ct) =>
            Results.Ok((await receitas.ListarAsync(ct)).Select(Resumo)));
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, IGerenciarReceitasProducao receitas, CancellationToken ct) =>
            await receitas.ObterAsync(id, ct) is { } receita ? Results.Ok(Detalhe(receita)) : Results.NotFound());
        group.MapGet("/{id:guid}/versoes/{numero:int}", async Task<IResult> (Guid id, int numero,
            IGerenciarReceitasProducao receitas, CancellationToken ct) =>
            (await receitas.ObterAsync(id, ct))?.Versoes.SingleOrDefault(v => v.Numero == numero) is { } versao
                ? Results.Ok(Versao(versao)) : Results.NotFound());
        group.MapPost("", async Task<IResult> (ReceitaInput input, HttpContext ctx,
            IGerenciarReceitasProducao receitas, CancellationToken ct) =>
            Autor(ctx, out var autor) ? Resultado(await receitas.SalvarAsync(null, input, autor, ct), true) : Results.Forbid())
            .RequireXaquiamuniAntiforgery();
        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, ReceitaInput input, HttpContext ctx,
            IGerenciarReceitasProducao receitas, CancellationToken ct) =>
            Autor(ctx, out var autor) ? Resultado(await receitas.SalvarAsync(id, input, autor, ct)) : Results.Forbid())
            .RequireXaquiamuniAntiforgery();
        group.MapPatch("/{id:guid}/status", async Task<IResult> (Guid id, StatusInput input, HttpContext ctx,
            IGerenciarReceitasProducao receitas, CancellationToken ct) =>
            !Autor(ctx, out var autor) ? Results.Forbid() : input.Ativa is bool ativa
                ? Resultado(await receitas.AlterarStatusAsync(id, ativa, input.Token, autor, ct))
                : Invalida("Informe a situação da receita."))
            .RequireXaquiamuniAntiforgery();
    }

    private static IResult Resultado(ReceitaResult resultado, bool criado = false) => resultado.Status switch
    {
        ReceitaStatus.Sucesso when criado => Results.Created($"{Rota}/{resultado.Receita!.Id:D}", Detalhe(resultado.Receita)),
        ReceitaStatus.Sucesso => Results.Ok(Detalhe(resultado.Receita!)),
        ReceitaStatus.Ausente => Results.NotFound(),
        ReceitaStatus.Conflito => Results.Problem(statusCode: 409, detail: resultado.Erro),
        _ => Invalida(resultado.Erro ?? "Confira os dados da receita.")
    };

    private static IResult Invalida(string mensagem) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["receita"] = [mensagem] });

    private static bool Autor(HttpContext ctx, out Guid id) =>
        Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;

    private static ResumoReceita Resumo(ReceitaProducao r) => new(r.Id, r.Nome, r.Ativa, r.Variante.ProdutoId,
        r.Variante.Produto.Nome, r.ProdutoVarianteId, r.Variante.Identificacao, r.Atual.Numero, r.Atual.Rendimento);

    private static DetalheReceita Detalhe(ReceitaProducao r) => new(Resumo(r), r.Token, Versao(r.Atual),
        r.Versoes.OrderByDescending(v => v.Numero).Select(v => new Revisao(v.Numero, v.Rendimento, v.CriadaEmUtc)).ToArray());

    private static VersaoReceita Versao(ReceitaProducaoVersao v) => new(v.Numero, v.Rendimento, v.CriadaEmUtc,
        v.Itens.OrderBy(i => i.NomeInsumo).ThenBy(i => i.InsumoId).Select(i => new ItemReceita(i.InsumoId, i.NomeInsumo,
            i.Quantidade.ToString("0.######", CultureInfo.InvariantCulture), i.UnidadeBase.ToString())).ToArray());

    public sealed record StatusInput(bool? Ativa, Guid Token);
    public sealed record ResumoReceita(Guid Id, string Nome, bool Ativa, Guid ProdutoId, string Produto, Guid VarianteId,
        string Variante, int NumeroVersao, int Rendimento);
    public sealed record DetalheReceita(ResumoReceita Resumo, Guid Token, VersaoReceita Atual, IReadOnlyList<Revisao> Historico);
    public sealed record Revisao(int Numero, int Rendimento, DateTimeOffset CriadaEmUtc);
    public sealed record VersaoReceita(int Numero, int Rendimento, DateTimeOffset CriadaEmUtc, IReadOnlyList<ItemReceita> Itens);
    public sealed record ItemReceita(Guid InsumoId, string NomeInsumo, string Quantidade, string UnidadeBase);
}
