namespace Xaquiamuni.Dominio.Entidades;

public enum TipoPromocao { Percentual, PrecoFixo }
public enum EscopoPromocao { TodasVariantes, VariantesSelecionadas }

public sealed class Promocao
{
    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public TipoPromocao Tipo { get; private set; }
    public EscopoPromocao Escopo { get; private set; }
    public long Valor { get; private set; }
    public DateTimeOffset? InicioUtc { get; private set; }
    public DateTimeOffset? FimUtc { get; private set; }
    public bool Ativa { get; private set; }
    public Guid CriadaPorId { get; private set; }
    public DateTimeOffset CriadaEmUtc { get; private set; }
    public Guid AlteradaPorId { get; private set; }
    public DateTimeOffset AlteradaEmUtc { get; private set; }
    public int Versao { get; private set; }
    public ICollection<PromocaoVariante> Variantes { get; } = new List<PromocaoVariante>();

    public static Promocao Criar(Guid produtoId, Guid autor, DateTimeOffset agora) => new()
    {
        Id = Guid.NewGuid(), ProdutoId = produtoId, CriadaPorId = autor, CriadaEmUtc = agora
    };

    public void Configurar(TipoPromocao tipo, EscopoPromocao escopo, long valor,
        DateTimeOffset? inicio, DateTimeOffset? fim, IReadOnlyCollection<Guid> variantes)
    {
        if (!Enum.IsDefined(tipo) || !Enum.IsDefined(escopo) || valor <= 0 ||
            (tipo == TipoPromocao.Percentual && valor >= 10_000) || inicio >= fim ||
            (escopo == EscopoPromocao.TodasVariantes) != (variantes.Count == 0) ||
            variantes.Distinct().Count() != variantes.Count)
            throw new ArgumentException("Configuração de promoção inválida.");

        Tipo = tipo;
        Escopo = escopo;
        Valor = valor;
        InicioUtc = inicio?.ToUniversalTime();
        FimUtc = fim?.ToUniversalTime();
        foreach (var removida in Variantes.Where(v => !variantes.Contains(v.VarianteId)).ToArray())
            Variantes.Remove(removida);
        foreach (var id in variantes.Except(Variantes.Select(v => v.VarianteId)))
            Variantes.Add(new(Id, ProdutoId, id));
    }

    public void RegistrarAlteracao(bool ativa, Guid autor, DateTimeOffset agora)
    {
        Ativa = ativa;
        AlteradaPorId = autor;
        AlteradaEmUtc = agora;
        Versao = checked(Versao + 1);
    }

    public bool VigenteEm(DateTimeOffset instante) =>
        Ativa && (InicioUtc is null || InicioUtc <= instante) && (FimUtc is null || instante < FimUtc);

    public bool Sobrepoe(Promocao outra) =>
        (FimUtc is null || outra.InicioUtc is null || outra.InicioUtc < FimUtc) &&
        (outra.FimUtc is null || InicioUtc is null || InicioUtc < outra.FimUtc);

    public bool Abrange(Guid varianteId) =>
        Escopo == EscopoPromocao.TodasVariantes || Variantes.Any(v => v.VarianteId == varianteId);
}

public sealed record PromocaoVariante(Guid PromocaoId, Guid ProdutoId, Guid VarianteId);
