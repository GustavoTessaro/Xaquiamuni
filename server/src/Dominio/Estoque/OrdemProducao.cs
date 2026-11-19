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
    public string? NomeProduto { get; private set; }
    public string? NomeVariante { get; private set; }
    public string? NomeReceita { get; private set; }
    public Guid? ChaveInicio { get; private set; }
    public string? ImpressaoInicio { get; private set; }
    public Guid Versao { get; private set; } = Guid.NewGuid();
    public Guid? ChaveConclusao { get; private set; }
    public string? ImpressaoConclusao { get; private set; }
    public int? QuantidadeProduzida { get; private set; }
    public Guid? MovimentoAcabadoId { get; private set; }
    public Guid? ChaveCancelamento { get; private set; }
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
            Id = Guid.NewGuid(), ProdutoVarianteId = receita.ProdutoVarianteId, NomeReceita = receita.Nome,
            NomeProduto = receita.Variante.Produto?.Nome, NomeVariante = receita.Variante.Identificacao,
            ReceitaVersaoId = versao.Id, QuantidadePlanejada = quantidade,
            Situacao = SituacaoOrdemProducao.EmAndamento, IniciadaPorId = autor, IniciadaEmUtc = agora.ToUniversalTime()
        };

        var snapshots = versao.Itens.ToDictionary(i => i.InsumoId);
        ordem._necessidades.AddRange(calculadas.Select(n => new NecessidadeOrdemProducao(
            ordem, snapshots[n.InsumoId], n.Quantidade)));
        return ordem;
    }

    public void RegistrarTentativaInicio(Guid chave, string impressao)
    {
        if (chave == Guid.Empty || impressao.Length != 64 || ChaveInicio.HasValue)
            throw new ArgumentException("Tentativa de início inválida ou já registrada.");
        ChaveInicio = chave;
        ImpressaoInicio = impressao;
    }

    public void Cancelar(Guid autor, DateTimeOffset agora, Guid? chave = null)
    {
        if (Situacao == SituacaoOrdemProducao.Cancelada) return;
        if (Situacao != SituacaoOrdemProducao.EmAndamento || autor == Guid.Empty || agora < IniciadaEmUtc)
            throw new InvalidOperationException("A ordem não pode ser cancelada com os dados informados.");
        Situacao = SituacaoOrdemProducao.Cancelada;
        Versao = Guid.NewGuid();
        ChaveCancelamento = chave;
        EncerradaPorId = autor;
        EncerradaEmUtc = agora.ToUniversalTime();
    }

    public void Concluir(int quantidade, Guid movimento, Guid autor, Guid chave, string impressao, DateTimeOffset agora)
    {
        if (Situacao != SituacaoOrdemProducao.EmAndamento || agora < IniciadaEmUtc)
            throw new InvalidOperationException("Só é possível concluir uma ordem em andamento.");
        if (quantidade <= 0 || movimento == Guid.Empty || autor == Guid.Empty || chave == Guid.Empty || impressao.Length != 64)
            throw new ArgumentException("Dados de conclusão inválidos.");
        Situacao = SituacaoOrdemProducao.Concluida;
        QuantidadeProduzida = quantidade;
        MovimentoAcabadoId = movimento;
        ChaveConclusao = chave;
        ImpressaoConclusao = impressao;
        EncerradaPorId = autor;
        EncerradaEmUtc = agora.ToUniversalTime();
        Versao = Guid.NewGuid();
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

    public Guid? MovimentoConsumoId { get; private set; }

    private AlocacaoMateriaPrima() { }

    public void RegistrarConsumo(MovimentoInsumo movimento)
    {
        if (MovimentoConsumoId.HasValue || movimento.InsumoId != Necessidade.InsumoId ||
            movimento.LoteId != Saldo.LoteId || movimento.Direcao != DirecaoMovimentoInsumo.Saida ||
            movimento.QuantidadeBase.UnidadeBase != Necessidade.UnidadeBase ||
            movimento.QuantidadeBase.ValorBase <= 0m || movimento.QuantidadeBase.ValorBase > Reservada)
            throw new ArgumentException("O consumo não corresponde à reserva da posição.");
        Consumida = movimento.QuantidadeBase.ValorBase;
        MovimentoConsumoId = movimento.Id;
    }

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
