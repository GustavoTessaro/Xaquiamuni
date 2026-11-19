namespace Xaquiamuni.Dominio.Estoque;

public enum SituacaoOrdemProducao { EmAndamento, Concluida, Cancelada }

public sealed class OrdemProducao
{
    private readonly List<NecessidadeOrdemProducao> _necessidades = [];
    public Guid Id { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public Guid ReceitaVersaoId { get; private set; }
    public int QuantidadePlanejada { get; private set; }
    public SituacaoOrdemProducao Situacao { get; private set; }
    public Guid IniciadaPorId { get; private set; }
    public DateTimeOffset IniciadaEmUtc { get; private set; }
    public Guid? EncerradaPorId { get; private set; }
    public DateTimeOffset? EncerradaEmUtc { get; private set; }
    public IReadOnlyList<NecessidadeOrdemProducao> Necessidades => _necessidades.AsReadOnly();

    private OrdemProducao() { }

    public static OrdemProducao Criar(ReceitaProducao receita, int quantidade, Guid autor, DateTimeOffset agora)
    {
        if (!receita.Ativa || !receita.Variante.Ativa)
            throw new ArgumentException("Escolha uma receita e variante ativas.");
        if (autor == Guid.Empty) throw new ArgumentException("Informe o responsável.");
        var versao = receita.Atual;
        var calculadas = versao.CalcularNecessidades(quantidade);
        var ordem = new OrdemProducao
        {
            Id = Guid.NewGuid(), ProdutoVarianteId = receita.ProdutoVarianteId,
            ReceitaVersaoId = versao.Id, QuantidadePlanejada = quantidade,
            Situacao = SituacaoOrdemProducao.EmAndamento, IniciadaPorId = autor, IniciadaEmUtc = agora.ToUniversalTime()
        };

        var snapshots = versao.Itens.ToDictionary(i => i.InsumoId);
        ordem._necessidades.AddRange(calculadas.Select(n => new NecessidadeOrdemProducao(
            ordem, snapshots[n.InsumoId], n.Quantidade)));
        return ordem;
    }

    public void Cancelar(Guid autor, DateTimeOffset agora)
    {
        if (Situacao == SituacaoOrdemProducao.Cancelada) return;
        if (Situacao != SituacaoOrdemProducao.EmAndamento || autor == Guid.Empty || agora < IniciadaEmUtc)
            throw new InvalidOperationException("A ordem não pode ser cancelada com os dados informados.");
        Situacao = SituacaoOrdemProducao.Cancelada;
        EncerradaPorId = autor;
        EncerradaEmUtc = agora.ToUniversalTime();
    }
}

public sealed class NecessidadeOrdemProducao
{
    public Guid Id { get; private set; }
    public Guid OrdemId { get; private set; }
    public OrdemProducao Ordem { get; private set; } = null!;
    public Guid InsumoId { get; private set; }
    public string NomeInsumo { get; private set; } = null!;
    public UnidadeFisicaEstoque UnidadeBase { get; private set; }
    public decimal Quantidade { get; private set; }

    private NecessidadeOrdemProducao() { }

    internal NecessidadeOrdemProducao(OrdemProducao ordem, ItemReceitaProducao item, QuantidadeEstoque quantidade)
    {
        Id = Guid.NewGuid();
        Ordem = ordem;
        OrdemId = ordem.Id;
        InsumoId = item.InsumoId;
        NomeInsumo = item.NomeInsumo;
        UnidadeBase = quantidade.UnidadeBase;
        Quantidade = quantidade.ValorBase;
    }
}

public sealed class AlocacaoMateriaPrima
{
    public Guid NecessidadeId { get; private set; }
    public NecessidadeOrdemProducao Necessidade { get; private set; } = null!;
    public Guid SaldoId { get; private set; }
    public SaldoInsumo Saldo { get; private set; } = null!;
    public decimal Reservada { get; private set; }
    public decimal Consumida { get; private set; }

    private AlocacaoMateriaPrima() { }

    public AlocacaoMateriaPrima(NecessidadeOrdemProducao necessidade, SaldoInsumo saldo, decimal quantidade)
    {
        if (necessidade.InsumoId != saldo.InsumoId || necessidade.UnidadeBase != saldo.QuantidadeFisica.UnidadeBase ||
            quantidade > necessidade.Quantidade || quantidade > saldo.QuantidadeFisica.ValorBase)
            throw new ArgumentException("A posição ou quantidade não corresponde à necessidade.");
        Reservada = QuantidadeEstoque.Criar(quantidade, necessidade.UnidadeBase).ExigirPositiva().ValorBase;
        Necessidade = necessidade;
        NecessidadeId = necessidade.Id;
        SaldoId = saldo.Id;
    }
}
