using System.Security.Claims;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Autorizacao;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Api.Catalogo;

public static class AdminFornecedorEndpoints
{
    private const string BasePath = "/api/admin/fornecedores";

    public static void MapAdminFornecedorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthorizationPolicies.Administracao);
        group.MapGet("", async Task<IResult> (string? busca, bool? ativo, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            busca?.Length > Fornecedor.NomeMax ? Invalido("A busca excede o limite de caracteres.") :
                Results.Ok((await fornecedores.ListarAsync(busca, ativo, ct)).Select(Resposta.From).ToArray()));
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, HttpContext contexto, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
        {
            contexto.Response.Headers.CacheControl = "private, no-store";
            if (await fornecedores.ConsultarAsync(id, ct) is not { } fornecedor) return Results.NotFound();
            contexto.Response.Headers.ETag = $"\"{fornecedor.Versao:D}\"";
            return Results.Ok(Resposta.From(fornecedor));
        });
        group.MapPost("", async (DadosFornecedor dados, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            Resultado(await fornecedores.CadastrarAsync(dados, ct), criado: true)).RequireXaquiamuniAntiforgery();
        group.MapPut("/{id:guid}", async (Guid id, HttpContext contexto, DadosFornecedor dados, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            LerVersao(contexto, out var versao) ? Resultado(await fornecedores.EditarAsync(id, dados, versao, ct)) :
                VersaoObrigatoria()).RequireXaquiamuniAntiforgery();
        group.MapPatch("/{id:guid}/status", async (Guid id, StatusRequest dados, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            dados.Ativo is bool ativo ? Resultado(await fornecedores.AlterarStatusAsync(id, ativo, ct)) :
                Invalido("Informe a situação do fornecedor.")).RequireXaquiamuniAntiforgery();
        group.MapDelete("/{id:guid}", ExcluirAsync).RequireXaquiamuniAntiforgery();
    }

    private static async Task<IResult> ExcluirAsync(Guid id, HttpContext contexto, IGerenciarFornecedores fornecedores, CancellationToken ct)
    {
        contexto.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var autor) || autor == Guid.Empty)
            return Results.Forbid();
        if (id == Guid.Empty) return Invalido("Informe um fornecedor válido.");
        if (!LerVersao(contexto, out var versao)) return VersaoObrigatoria();
        var chaves = contexto.Request.Headers["Idempotency-Key"];
        if (chaves.Count != 1 || !Guid.TryParseExact(chaves[0], "D", out var chave) || chave == Guid.Empty)
            return Invalido("Informe uma Idempotency-Key UUID válida.");
        var resultado = await fornecedores.ExcluirAsync(id, versao, autor, chave, ct);
        return resultado.Status == FornecedorStatus.Sucesso ? Results.NoContent() : Resultado(resultado);
    }

    private static bool LerVersao(HttpContext contexto, out Guid versao)
    {
        versao = Guid.Empty;
        var tags = contexto.Request.Headers.IfMatch;
        return tags.Count == 1 && tags[0] is { Length: 38 } tag && tag[0] == '"' && tag[^1] == '"' &&
            Guid.TryParseExact(tag.AsSpan(1, 36), "D", out versao) && versao != Guid.Empty;
    }

    private static IResult VersaoObrigatoria() => Results.Problem(statusCode: 400,
        detail: "Informe a versão atual no cabeçalho If-Match.",
        extensions: new Dictionary<string, object?> { ["codigo"] = "versao_invalida" });

    private static IResult Conflito(string codigo, string mensagem) => Results.Problem(statusCode: 409, detail: mensagem,
        extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

    private static IResult Resultado(ResultadoFornecedor resultado, bool criado = false) => resultado.Status switch
    {
        FornecedorStatus.Sucesso when criado =>
            Results.Created($"{BasePath}/{resultado.Fornecedor!.Id}", Resposta.From(resultado.Fornecedor)),
        FornecedorStatus.Sucesso => Results.Ok(Resposta.From(resultado.Fornecedor!)),
        FornecedorStatus.NaoEncontrado => Results.NotFound(),
        FornecedorStatus.NomeDuplicado => Results.Problem(statusCode: 409, detail: resultado.Erro),
        FornecedorStatus.VersaoDesatualizada => Conflito("versao_desatualizada", "O fornecedor foi alterado. Recarregue os dados."),
        FornecedorStatus.ComCompras => Conflito("fornecedor_com_compras", "Há compras vinculadas a este fornecedor."),
        FornecedorStatus.ComHistorico => Conflito("fornecedor_com_historico", "Este fornecedor já foi utilizado em uma compra."),
        FornecedorStatus.HistoricoInconclusivo => Conflito("fornecedor_historico_inconclusivo", "Não foi possível confirmar que este fornecedor nunca foi utilizado."),
        FornecedorStatus.ChaveEmUso => Conflito("chave_em_uso", "A chave já foi utilizada com outros parâmetros."),
        FornecedorStatus.ConflitoConcorrencia => Conflito("conflito_concorrencia", "O cadastro está sendo alterado. Recarregue os dados."),
        _ => Invalido(resultado.Erro ?? "Confira os dados do fornecedor.")
    };

    private static IResult Invalido(string erro) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["fornecedor"] = [erro] });

    public sealed record StatusRequest(bool? Ativo);
    public sealed record Resposta(Guid Id, string Nome, string? Telefone, string? Email, string? Observacoes,
        bool Ativo, DateTimeOffset CriadoEmUtc, DateTimeOffset AtualizadoEmUtc, Guid Versao)
    {
        public static Resposta From(Fornecedor f) =>
            new(f.Id, f.Nome, f.Telefone, f.Email, f.Observacoes, f.Ativo, f.CriadoEmUtc, f.AtualizadoEmUtc, f.Versao);
    }
}
