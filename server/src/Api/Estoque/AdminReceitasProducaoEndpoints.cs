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
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, HttpContext ctx, IGerenciarReceitasProducao receitas, CancellationToken ct) =>
        {
            if (await receitas.ObterAsync(id, ct) is not { } receita) return Results.NotFound();
            ctx.Response.Headers.ETag = $"\"{receita.Token:D}\"";
            return Results.Ok(Detalhe(receita));
        });
        group.MapDelete("/{id:guid}", (Guid id, HttpContext ctx, IExcluirReceita exclusao, CancellationToken ct) =>
            Excluir(id, null, ctx, exclusao, ct)).RequireXaquiamuniAntiforgery();
        group.MapDelete("/{id:guid}/versoes/{versaoId:guid}", (Guid id, Guid versaoId, HttpContext ctx,
            IExcluirReceita exclusao, CancellationToken ct) =>
            Excluir(id, versaoId, ctx, exclusao, ct)).RequireXaquiamuniAntiforgery();
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

    private static async Task<IResult> Excluir(Guid id, Guid? revisao, HttpContext ctx, IExcluirReceita exclusao, CancellationToken ct)
    {
        if (!Autor(ctx, out var autor)) return Results.Forbid();
        if (id == Guid.Empty || revisao == Guid.Empty)
            return Invalida("Informe identificadores válidos para a exclusão.");
        var ifMatch = ctx.Request.Headers.IfMatch.ToString();
        if (ifMatch.Length != 38 || ifMatch[0] != '"' || ifMatch[^1] != '"' ||
            !Guid.TryParseExact(ifMatch[1..^1], "D", out var token) || token == Guid.Empty)
            return Problema(400, "versao_invalida", "Informe a versão atual em If-Match.");
        if (!Guid.TryParseExact(ctx.Request.Headers["Idempotency-Key"].ToString(), "D", out var chave) || chave == Guid.Empty)
            return Problema(400, "chave_invalida", "Informe uma chave de idempotência UUID válida.");

        var (status, codigo) = await exclusao.ExecutarAsync(id, revisao, token, autor, chave, ct);
        return status == 204 ? Results.NoContent() : Problema(status, codigo!, codigo switch
        {
            "receita_nao_encontrada" => "Receita não encontrada.",
            "versao_receita_nao_encontrada" => "Versão não encontrada nesta receita.",
            "versao_desatualizada" => "A receita foi alterada. Recarregue a ficha e confirme novamente.",
            "chave_em_uso" => "A chave já foi usada com outros dados.",
            "receita_com_producao" => "Uma versão desta receita foi utilizada em produção. Você pode inativar a receita.",
            "versao_receita_com_producao" => "Esta versão foi utilizada em produção e precisa ser preservada.",
            "ultima_versao_receita" => "A receita precisa manter pelo menos uma versão.",
            "versao_receita_atual" => "A versão atual é necessária para a receita. Você pode inativar a receita.",
            "receita_historico_inconclusivo" => "Não foi possível verificar o histórico desta receita com segurança.",
            _ => "Outra operação alterou o cadastro. Atualize a ficha antes de continuar."
        });
    }

    private static IResult Problema(int status, string codigo, string mensagem) =>
        Results.Problem(statusCode: status, detail: mensagem, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

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
        r.Versoes.OrderByDescending(v => v.Numero).Select(v => new Revisao(v.Numero, v.Rendimento, v.CriadaEmUtc, v.Id)).ToArray());

    private static VersaoReceita Versao(ReceitaProducaoVersao v) => new(v.Numero, v.Rendimento, v.CriadaEmUtc,
        v.Itens.OrderBy(i => i.NomeInsumo).ThenBy(i => i.InsumoId).Select(i => new ItemReceita(i.InsumoId, i.NomeInsumo,
            i.Quantidade.ToString("0.######", CultureInfo.InvariantCulture), i.UnidadeBase.ToString())).ToArray(), v.Id);

    public sealed record StatusInput(bool? Ativa, Guid Token);
    public sealed record ResumoReceita(Guid Id, string Nome, bool Ativa, Guid ProdutoId, string Produto, Guid VarianteId,
        string Variante, int NumeroVersao, int Rendimento);
    public sealed record DetalheReceita(ResumoReceita Resumo, Guid Token, VersaoReceita Atual, IReadOnlyList<Revisao> Historico);
    public sealed record Revisao(int Numero, int Rendimento, DateTimeOffset CriadaEmUtc, Guid Id);
    public sealed record VersaoReceita(int Numero, int Rendimento, DateTimeOffset CriadaEmUtc, IReadOnlyList<ItemReceita> Itens, Guid Id);
    public sealed record ItemReceita(Guid InsumoId, string NomeInsumo, string Quantidade, string UnidadeBase);
}
