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
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            await fornecedores.ConsultarAsync(id, ct) is { } fornecedor
                ? Results.Ok(Resposta.From(fornecedor)) : Results.NotFound());
        group.MapPost("", async (DadosFornecedor dados, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            Resultado(await fornecedores.CadastrarAsync(dados, ct), criado: true)).RequireXaquiamuniAntiforgery();
        group.MapPut("/{id:guid}", async (Guid id, DadosFornecedor dados, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            Resultado(await fornecedores.EditarAsync(id, dados, ct))).RequireXaquiamuniAntiforgery();
        group.MapPatch("/{id:guid}/status", async (Guid id, StatusRequest dados, IGerenciarFornecedores fornecedores, CancellationToken ct) =>
            dados.Ativo is bool ativo ? Resultado(await fornecedores.AlterarStatusAsync(id, ativo, ct)) :
                Invalido("Informe a situação do fornecedor.")).RequireXaquiamuniAntiforgery();
    }

    private static IResult Resultado(ResultadoFornecedor resultado, bool criado = false) => resultado.Status switch
    {
        FornecedorStatus.Sucesso when criado =>
            Results.Created($"{BasePath}/{resultado.Fornecedor!.Id}", Resposta.From(resultado.Fornecedor)),
        FornecedorStatus.Sucesso => Results.Ok(Resposta.From(resultado.Fornecedor!)),
        FornecedorStatus.NaoEncontrado => Results.NotFound(),
        FornecedorStatus.NomeDuplicado => Results.Problem(statusCode: 409, detail: resultado.Erro),
        _ => Invalido(resultado.Erro ?? "Confira os dados do fornecedor.")
    };

    private static IResult Invalido(string erro) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["fornecedor"] = [erro] });

    public sealed record StatusRequest(bool? Ativo);
    public sealed record Resposta(Guid Id, string Nome, string? Telefone, string? Email, string? Observacoes,
        bool Ativo, DateTimeOffset CriadoEmUtc, DateTimeOffset AtualizadoEmUtc)
    {
        public static Resposta From(Fornecedor f) =>
            new(f.Id, f.Nome, f.Telefone, f.Email, f.Observacoes, f.Ativo, f.CriadoEmUtc, f.AtualizadoEmUtc);
    }
}
