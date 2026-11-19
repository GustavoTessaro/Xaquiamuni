using System.Numerics;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class ReceitaProducao
{
    public const int NomeMax = 120;
    private readonly List<ReceitaProducaoVersao> _versoes = [];
    public Guid Id { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public ProdutoVariante Variante { get; private set; } = null!;
    public string Nome { get; private set; } = null!;
    public bool Ativa { get; private set; }
    public Guid Token { get; private set; }
    public Guid CriadaPorId { get; private set; }
    public DateTimeOffset CriadaEmUtc { get; private set; }
    public Guid AtualizadaPorId { get; private set; }
    public DateTimeOffset AtualizadaEmUtc { get; private set; }
    public IReadOnlyList<ReceitaProducaoVersao> Versoes => _versoes.AsReadOnly();
    public ReceitaProducaoVersao Atual => _versoes.MaxBy(v => v.Numero)!;

    private ReceitaProducao() { }

    public static ReceitaProducao Criar(ProdutoVariante variante, string? nome, int rendimento,
        IReadOnlyList<ItemReceitaProducao> itens, Guid autor, DateTimeOffset agora)
    {
        if (!variante.Ativa) throw new ArgumentException("Selecione uma variante ativa.");
        var receita = new ReceitaProducao
        {
            Id = Guid.NewGuid(), Variante = variante, ProdutoVarianteId = variante.Id, Ativa = true,
            CriadaPorId = autor, CriadaEmUtc = agora.ToUniversalTime()
        };
        receita.Editar(nome, rendimento, itens, autor, agora);
        return receita;
    }

    public ReceitaProducaoVersao? Editar(string? nome, int rendimento, IReadOnlyList<ItemReceitaProducao> itens,
        Guid autor, DateTimeOffset agora)
    {
        var nomeLimpo = nome?.Trim();
        if (string.IsNullOrEmpty(nomeLimpo) || nomeLimpo.Length > NomeMax)
            throw new ArgumentException($"Informe um nome com até {NomeMax} caracteres.");
        if (autor == Guid.Empty) throw new ArgumentException("Informe o responsável.");
        ReceitaProducaoVersao.Validar(rendimento, itens);
        var anterior = _versoes.MaxBy(v => v.Numero);
        ReceitaProducaoVersao? nova = null;
        if (anterior is null || anterior.Rendimento != rendimento || anterior.Itens.Count != itens.Count ||
            !itens.All(item => anterior.Itens.Any(item.MesmaQuantidade)))
        {
            nova = new ReceitaProducaoVersao(checked((anterior?.Numero ?? 0) + 1), rendimento,
                itens, autor, agora);
            _versoes.Add(nova);
        }
        Nome = nomeLimpo;
        Auditar(autor, agora);
        return nova;
    }

    public void DefinirAtiva(bool ativa, Guid autor, DateTimeOffset agora)
    {
        if (Ativa == ativa) return;
        if (ativa && !Variante.Ativa) throw new ArgumentException("A variante da receita está inativa.");
        Ativa = ativa;
        Auditar(autor, agora);
    }

    private void Auditar(Guid autor, DateTimeOffset agora)
    {
        Token = Guid.NewGuid();
        AtualizadaPorId = autor;
        AtualizadaEmUtc = agora.ToUniversalTime();
    }
}

public sealed class ReceitaProducaoVersao
{
    private readonly List<ItemReceitaProducao> _itens = [];
    public Guid Id { get; private set; }
    public int Numero { get; private set; }
    public int Rendimento { get; private set; }
    public Guid CriadaPorId { get; private set; }
    public DateTimeOffset CriadaEmUtc { get; private set; }
    public IReadOnlyList<ItemReceitaProducao> Itens => _itens.AsReadOnly();

    private ReceitaProducaoVersao() { }

    internal ReceitaProducaoVersao(int numero, int rendimento, IReadOnlyList<ItemReceitaProducao> itens,
        Guid autor, DateTimeOffset agora)
    {
        Id = Guid.NewGuid();
        Numero = numero;
        Rendimento = rendimento;
        CriadaPorId = autor;
        CriadaEmUtc = agora.ToUniversalTime();
        _itens.AddRange(itens.Select(i => i.Copiar()));
    }

    internal static void Validar(int rendimento, IReadOnlyList<ItemReceitaProducao> itens)
    {
        if (rendimento <= 0 || itens.Count is < 1 or > 100)
            throw new ArgumentException("Informe um rendimento inteiro positivo e de 1 a 100 materiais.");
        if (itens.Select(i => i.InsumoId).Distinct().Count() != itens.Count)
            throw new ArgumentException("Cada material pode aparecer apenas uma vez na receita.");
    }

    public IReadOnlyList<NecessidadeReceita> CalcularNecessidades(int quantidadePlanejada)
    {
        if (quantidadePlanejada <= 0) throw new ArgumentException("Informe uma quantidade planejada inteira positiva.");
        return _itens.Select(item =>
        {
            // Trabalha com a menor unidade representável para detectar divisões inexatas.
            var escala = item.UnidadeBase == UnidadeFisicaEstoque.Unidade ? 1m : 1_000_000m;
            var numerador = new BigInteger(item.Quantidade * escala) * quantidadePlanejada;
            var resultado = BigInteger.DivRem(numerador, Rendimento, out var resto);
            if (!resto.IsZero)
                throw new ArgumentException($"A quantidade planejada não gera uma medida exata para {item.NomeInsumo}.");
            if (resultado > new BigInteger(QuantidadeEstoque.MaximoNaUnidadeBase * escala))
                throw new ArgumentException($"A quantidade necessária de {item.NomeInsumo} excede o limite suportado.");
            return new NecessidadeReceita(item.InsumoId, QuantidadeEstoque.Criar((decimal)resultado / escala, item.UnidadeBase));
        }).ToArray();
    }
}

public sealed class ItemReceitaProducao
{
    public Guid Id { get; private set; }
    public Guid InsumoId { get; private set; }
    public string NomeInsumo { get; private set; } = null!;
    public UnidadeFisicaEstoque UnidadeBase { get; private set; }
    public decimal Quantidade { get; private set; }

    private ItemReceitaProducao() { }

    public static ItemReceitaProducao Criar(InsumoEstoque insumo, decimal quantidade, UnidadeFisicaEstoque unidade)
    {
        if (!insumo.Ativo) throw new ArgumentException("Selecione um material ativo.");
        if (unidade != insumo.UnidadeBase) throw new ArgumentException("Use a unidade-base do material.");
        return new ItemReceitaProducao
        {
            InsumoId = insumo.Id, NomeInsumo = insumo.Nome, UnidadeBase = unidade,
            Quantidade = QuantidadeEstoque.Criar(quantidade, unidade).ExigirPositiva().ValorBase
        };
    }

    internal bool MesmaQuantidade(ItemReceitaProducao outro) =>
        InsumoId == outro.InsumoId && UnidadeBase == outro.UnidadeBase && Quantidade == outro.Quantidade;

    internal ItemReceitaProducao Copiar()
    {
        var item = (ItemReceitaProducao)MemberwiseClone();
        item.Id = Guid.NewGuid();
        return item;
    }
}

public sealed record NecessidadeReceita(Guid InsumoId, QuantidadeEstoque Quantidade);
