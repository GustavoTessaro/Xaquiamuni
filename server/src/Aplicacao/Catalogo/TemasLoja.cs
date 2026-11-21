namespace Xaquiamuni.Aplicacao.Catalogo;

public static class TemasLoja
{
    public const string Original = "original";

    public static IReadOnlyList<TemaLoja> Catalogo { get; } = Array.AsReadOnly<TemaLoja>(
    [
        new(Original, "Original", "Identidade visual da Xaquiamuni.", true),
        new("natal", "Natal", "Tema comemorativo de Natal.", false),
        new("black-friday", "Black Friday", "Tema comemorativo de Black Friday.", false),
        new("pascoa", "Páscoa", "Tema comemorativo de Páscoa.", false),
        new("ano-novo", "Ano Novo", "Tema comemorativo de Ano Novo.", false),
        new("dia-das-maes", "Dia das Mães", "Tema comemorativo de Dia das Mães.", false),
    ]);

    public static TemaLoja? Buscar(string? chave) => Catalogo.SingleOrDefault(tema => tema.Chave == chave);
}

public sealed record TemaLoja(string Chave, string Nome, string Descricao, bool Disponivel);

public sealed record PublicacaoTemaLoja(string Chave, int Revisao, DateTimeOffset? PublicadoEmUtc, Guid? AdministradorId)
{
    public static PublicacaoTemaLoja Inicial { get; } = new(TemasLoja.Original, 0, null, null);
}

public interface IPublicacaoTemaLoja
{
    Task<PublicacaoTemaLoja> ConsultarAsync(CancellationToken ct);
    Task<PublicacaoTemaLoja?> PublicarAsync(string chave, int revisaoEsperada, Guid administradorId,
        Guid idempotencia, CancellationToken ct);
}
