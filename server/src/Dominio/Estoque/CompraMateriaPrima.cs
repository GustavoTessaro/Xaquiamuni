using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Dominio.Estoque;

public enum SituacaoCompraMateriaPrima { Rascunho, Cancelada, Recebida }

public sealed class CompraMateriaPrima
{
    private readonly List<LinhaCompraMateriaPrima> _linhas = [];
    public Guid Id { get; private set; }
    public Guid FornecedorId { get; private set; }
    public string NomeFornecedor { get; private set; } = null!;
    public DateOnly DataCompra { get; private set; }
    public SituacaoCompraMateriaPrima Situacao { get; private set; }
    public decimal? Total { get; private set; }
    public Guid CriadoPorId { get; private set; }
    public Guid AtualizadoPorId { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset AtualizadoEmUtc { get; private set; }
    public DateTimeOffset? CanceladoEmUtc { get; private set; }
    public DateTimeOffset? RecebidaEmUtc { get; private set; }
    public Guid? RecebidaPorId { get; private set; }
    public Guid? ChaveRecebimento { get; private set; }
    public string? ImpressaoRecebimento { get; private set; }
    public Guid Versao { get; private set; }
    public Guid ChaveCriacao { get; private set; }
    public string ImpressaoCriacao { get; private set; } = null!;
    public IReadOnlyList<LinhaCompraMateriaPrima> Linhas => _linhas.AsReadOnly();

    private CompraMateriaPrima() { }

    public static CompraMateriaPrima Criar(Fornecedor fornecedor, DateOnly data,
        IReadOnlyList<LinhaCompraMateriaPrima> linhas, Guid autor, Guid chave, string impressao, DateTimeOffset agora)
    {
        var compra = new CompraMateriaPrima
        {
            Id = Guid.NewGuid(), CriadoPorId = autor, CriadoEmUtc = agora.ToUniversalTime(),
            ChaveCriacao = chave, ImpressaoCriacao = impressao
        };
        compra.Editar(fornecedor, data, linhas, autor, agora);
        return compra;
    }

    public void Editar(Fornecedor fornecedor, DateOnly data, IReadOnlyList<LinhaCompraMateriaPrima> linhas,
        Guid autor, DateTimeOffset agora)
    {
        if (Situacao != SituacaoCompraMateriaPrima.Rascunho)
            throw new InvalidOperationException("Apenas rascunhos podem ser editados.");
        if (FornecedorId != fornecedor.Id && !fornecedor.Ativo)
            throw new ArgumentException("Selecione um fornecedor ativo.");
        if (data == default || linhas.Count is < 1 or > 50 || autor == Guid.Empty)
            throw new ArgumentException("Informe uma data, um responsável e de 1 a 50 linhas.");
        var total = linhas.Sum(linha => linha.ValorTotal ?? 0m);
        if (total > CustoAquisicaoInsumo.CustoTotalMaximo)
            throw new ArgumentException("O total da compra excede o limite suportado.");
        if (FornecedorId != fornecedor.Id)
        {
            FornecedorId = fornecedor.Id;
            NomeFornecedor = fornecedor.Nome;
        }
        DataCompra = data;
        Total = linhas.All(linha => linha.ValorTotal.HasValue) ? total : null;
        _linhas.Clear();
        _linhas.AddRange(linhas);
        Atualizar(autor, agora);
    }

    public void Cancelar(Guid autor, DateTimeOffset agora)
    {
        if (Situacao == SituacaoCompraMateriaPrima.Cancelada) return;
        if (Situacao != SituacaoCompraMateriaPrima.Rascunho)
            throw new InvalidOperationException("Apenas rascunhos podem ser cancelados.");
        Situacao = SituacaoCompraMateriaPrima.Cancelada;
        CanceladoEmUtc = agora.ToUniversalTime();
        Atualizar(autor, agora);
    }

    public void ConfirmarRecebimento(Guid autor, Guid chave, string impressao, DateTimeOffset agora)
    {
        if (Situacao != SituacaoCompraMateriaPrima.Rascunho)
            throw new InvalidOperationException("Apenas rascunhos podem ser recebidos.");
        if (autor == Guid.Empty || chave == Guid.Empty || string.IsNullOrWhiteSpace(impressao) ||
            _linhas.Count == 0 || _linhas.Any(linha => linha.Componentes.Count == 0 ||
                linha.Componentes.Any(c => c.Quantidade <= 0 || c.QuantidadeBase <= 0)))
            throw new ArgumentException("O recebimento exige responsável, chave e componentes válidos.");

        Situacao = SituacaoCompraMateriaPrima.Recebida;
        RecebidaEmUtc = agora.ToUniversalTime();
        RecebidaPorId = autor;
        ChaveRecebimento = chave;
        ImpressaoRecebimento = impressao;
        Atualizar(autor, agora);
    }

    private void Atualizar(Guid autor, DateTimeOffset agora)
    {
        AtualizadoPorId = autor;
        AtualizadoEmUtc = agora.ToUniversalTime();
        Versao = Guid.NewGuid();
    }
}

public sealed class LinhaCompraMateriaPrima
{
    private readonly List<ComponenteCompraMateriaPrima> _componentes = [];
    public Guid Id { get; private set; }
    public int Ordem { get; private set; }
    public string? Kit { get; private set; }
    public decimal Quantidade { get; private set; }
    public decimal? ValorTotal { get; private set; }
    public IReadOnlyList<ComponenteCompraMateriaPrima> Componentes => _componentes.AsReadOnly();

    private LinhaCompraMateriaPrima() { }

    public static LinhaCompraMateriaPrima Criar(int ordem, string? kit, decimal quantidade, decimal? valor,
        IReadOnlyList<ComponenteCompraMateriaPrima> componentes)
    {
        var nomeKit = string.IsNullOrWhiteSpace(kit) ? null : kit.Trim();
        if (nomeKit is null ? componentes.Count != 1 : componentes.Count is < 2 or > 30)
            throw new ArgumentException("Uma linha simples exige um material e um kit exige de 2 a 30 materiais.");
        if (nomeKit?.Length > 120 || quantidade <= 0 || quantidade > QuantidadeEstoque.MaximoNaUnidadeBase ||
            (nomeKit is null ? quantidade != componentes[0].Quantidade : quantidade != decimal.Truncate(quantidade)))
            throw new ArgumentException("Confira a quantidade da linha e, para kits, informe um nome e uma quantidade inteira.");
        // A quantidade de cada componente representa o total da linha, inclusive quando há vários kits.
        if (valor is decimal informado &&
            (informado < 0 || informado > CustoAquisicaoInsumo.CustoTotalMaximo ||
                decimal.Round(informado, 2, MidpointRounding.AwayFromZero) != informado))
            throw new ArgumentException("Informe um valor não negativo com até duas casas decimais.");
        var linha = new LinhaCompraMateriaPrima
        {
            Id = Guid.NewGuid(), Ordem = ordem, Kit = nomeKit, Quantidade = quantidade, ValorTotal = valor
        };
        linha._componentes.AddRange(componentes);
        return linha;
    }
}

public sealed class ComponenteCompraMateriaPrima
{
    public Guid Id { get; private set; }
    public int Ordem { get; private set; }
    public Guid InsumoId { get; private set; }
    public string NomeInsumo { get; private set; } = null!;
    public Guid? ApresentacaoId { get; private set; }
    public string? NomeApresentacao { get; private set; }
    public decimal Quantidade { get; private set; }
    public decimal? Fator { get; private set; }
    public decimal QuantidadeBase { get; private set; }
    public UnidadeFisicaEstoque UnidadeBase { get; private set; }

    private ComponenteCompraMateriaPrima() { }

    public static ComponenteCompraMateriaPrima Criar(int ordem, InsumoEstoque insumo, Guid? apresentacaoId,
        decimal quantidade)
    {
        if (!insumo.Ativo) throw new ArgumentException("Selecione um material ativo.");
        if (quantidade <= 0 || quantidade > QuantidadeEstoque.MaximoNaUnidadeBase)
            throw new ArgumentException("Informe uma quantidade positiva dentro do limite para cada material.");
        var conversao = apresentacaoId is Guid id ? insumo.ConverterCompra(id, quantidade) : null;
        var quantidadeBase = conversao?.QuantidadeBase ?? QuantidadeEstoque.Criar(quantidade, insumo.UnidadeBase);
        return new ComponenteCompraMateriaPrima
        {
            Id = Guid.NewGuid(), Ordem = ordem, InsumoId = insumo.Id, NomeInsumo = insumo.Nome,
            ApresentacaoId = apresentacaoId, NomeApresentacao = conversao?.NomeFormaCompra,
            Quantidade = quantidade, Fator = conversao?.FatorAplicado.ValorBase,
            QuantidadeBase = quantidadeBase.ValorBase, UnidadeBase = quantidadeBase.UnidadeBase
        };
    }

    public ResultadoConversaoCompraInsumo? ReconstituirConversao() =>
        ApresentacaoId is Guid apresentacao && Fator is decimal fator
            ? new ResultadoConversaoCompraInsumo(InsumoId, apresentacao, NomeApresentacao!, Quantidade,
                QuantidadeEstoque.Criar(fator, UnidadeBase), QuantidadeEstoque.Criar(QuantidadeBase, UnidadeBase))
            : null;

    public ComponenteCompraMateriaPrima CopiarSnapshot(int ordem)
    {
        var componente = (ComponenteCompraMateriaPrima)MemberwiseClone();
        componente.Id = Guid.NewGuid();
        componente.Ordem = ordem;
        return componente;
    }
}
