using System.Text;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class InsumoEstoque
{
    public const int NomeMaxLength = 200;

    private readonly List<FormaCompraInsumo> _formasCompra = [];

    private InsumoEstoque()
    {
    }

    private InsumoEstoque(
        Guid id,
        string nome,
        string nomeNormalizado,
        TipoInsumoEstoque tipo,
        UnidadeFisicaEstoque unidadeBase,
        QuantidadeEstoque? estoqueMinimo,
        bool controlaLote,
        bool validadeObrigatoria)
    {
        Id = id;
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        Tipo = tipo;
        UnidadeBase = unidadeBase;
        EstoqueMinimo = estoqueMinimo;
        ControlaLote = controlaLote;
        ValidadeObrigatoria = validadeObrigatoria;
        Ativo = true;
    }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = null!;
    public string NomeNormalizado { get; private set; } = null!;
    public TipoInsumoEstoque Tipo { get; private set; }
    public UnidadeFisicaEstoque UnidadeBase { get; private set; }
    public QuantidadeEstoque? EstoqueMinimo { get; private set; }
    public bool ControlaLote { get; private set; }
    public bool ValidadeObrigatoria { get; private set; }
    public bool Ativo { get; private set; }
    public IReadOnlyCollection<FormaCompraInsumo> FormasCompra => _formasCompra.AsReadOnly();

    public static InsumoEstoque Criar(
        string? nome,
        TipoInsumoEstoque tipo,
        UnidadeFisicaEstoque unidadeBase,
        QuantidadeEstoque? estoqueMinimo = null,
        bool controlaLote = false,
        bool validadeObrigatoria = false)
    {
        var normalizedName = NormalizarNome(nome);
        ValidarTipo(tipo);
        ValidarUnidadeBase(unidadeBase);
        ValidarEstoqueMinimo(estoqueMinimo, unidadeBase);
        ValidarConfiguracaoLote(controlaLote, validadeObrigatoria);

        return new InsumoEstoque(
            Guid.NewGuid(),
            normalizedName,
            normalizedName.ToUpperInvariant(),
            tipo,
            unidadeBase,
            estoqueMinimo,
            controlaLote,
            validadeObrigatoria);
    }

    public void AtualizarDados(
        string? nome,
        TipoInsumoEstoque tipo,
        QuantidadeEstoque? estoqueMinimo,
        bool controlaLote,
        bool validadeObrigatoria)
    {
        ValidarConfiguracaoLote(controlaLote, validadeObrigatoria);
        if (ControlaLote != controlaLote || ValidadeObrigatoria != validadeObrigatoria)
        {
            throw new InvalidOperationException(
                "A política de lotes e validade só pode ser alterada pela operação transacional protegida.");
        }

        AtualizarDadosValidandoDominio(nome, tipo, estoqueMinimo, controlaLote, validadeObrigatoria);
    }

    internal void AtualizarPoliticaLoteProtegida(
        bool controlaLote,
        bool validadeObrigatoria)
    {
        ValidarConfiguracaoLote(controlaLote, validadeObrigatoria);
        ControlaLote = controlaLote;
        ValidadeObrigatoria = validadeObrigatoria;
    }

    private void AtualizarDadosValidandoDominio(
        string? nome,
        TipoInsumoEstoque tipo,
        QuantidadeEstoque? estoqueMinimo,
        bool controlaLote,
        bool validadeObrigatoria)
    {
        var normalizedName = NormalizarNome(nome);
        ValidarTipo(tipo);
        ValidarEstoqueMinimo(estoqueMinimo, UnidadeBase);
        ValidarConfiguracaoLote(controlaLote, validadeObrigatoria);

        Nome = normalizedName;
        NomeNormalizado = normalizedName.ToUpperInvariant();
        Tipo = tipo;
        EstoqueMinimo = estoqueMinimo;
        ControlaLote = controlaLote;
        ValidadeObrigatoria = validadeObrigatoria;
    }

    public void DefinirAtivo(bool ativo)
    {
        Ativo = ativo;
    }

    public QuantidadeEstoque CalcularSaldoFisico(IEnumerable<SaldoInsumo> saldos)
    {
        ArgumentNullException.ThrowIfNull(saldos);

        var identidades = new HashSet<Guid?>();
        decimal total;
        try
        {
            total = 0m;
            foreach (var saldo in saldos)
            {
                if (saldo.InsumoId != Id)
                {
                    throw new ArgumentException(
                        "Todos os saldos devem pertencer ao insumo.",
                        nameof(saldos));
                }

                if (saldo.QuantidadeFisica.UnidadeBase != UnidadeBase)
                {
                    throw new InvalidOperationException(
                        "A unidade do saldo deve corresponder à unidade-base do insumo.");
                }

                if (ControlaLote != saldo.LoteId.HasValue)
                {
                    throw new InvalidOperationException(
                        "O tipo de saldo deve corresponder à configuração de controle por lote.");
                }

                if (!identidades.Add(saldo.LoteId))
                {
                    throw new InvalidOperationException(
                        "Há mais de um saldo para a mesma identidade de estoque.");
                }

                total = checked(total + saldo.QuantidadeFisica.ValorBase);
            }
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(saldos),
                "O saldo físico total excede o limite suportado.");
        }

        return QuantidadeEstoque.Criar(total, UnidadeBase);
    }

    public FormaCompraInsumo AdicionarFormaCompra(
        string? nome,
        decimal quantidadeEquivalente,
        UnidadeFisicaEstoque unidade)
    {
        var forma = FormaCompraInsumo.Criar(
            Id,
            nome,
            quantidadeEquivalente,
            unidade);
        ValidarFatorCompativel(forma.QuantidadeEquivalentePorEmbalagem, nameof(unidade));
        ValidarNomeFormaUnico(forma.NomeNormalizado);

        _formasCompra.Add(forma);
        return forma;
    }

    internal void AplicarMovimento(SaldoInsumo saldo, MovimentoInsumo movimento)
    {
        ArgumentNullException.ThrowIfNull(saldo);
        ArgumentNullException.ThrowIfNull(movimento);
        saldo.AplicarMovimento(movimento);
    }

    public void AtualizarFormaCompra(
        Guid formaCompraId,
        string? nome,
        decimal quantidadeEquivalente,
        UnidadeFisicaEstoque unidade)
    {
        var forma = ObterFormaCompra(formaCompraId);
        var nomeNormalizado = FormaCompraInsumo.NormalizarNome(nome);
        var fator = QuantidadeEstoque.Criar(quantidadeEquivalente, unidade)
            .ExigirPositiva(nameof(quantidadeEquivalente));
        ValidarFatorCompativel(fator, nameof(unidade));
        ValidarNomeFormaUnico(nomeNormalizado.ToUpperInvariant(), formaCompraId);

        forma.AtualizarDados(nomeNormalizado, nomeNormalizado.ToUpperInvariant(), fator);
    }

    public void DefinirFormaCompraAtiva(Guid formaCompraId, bool ativa)
    {
        ObterFormaCompra(formaCompraId).DefinirAtiva(ativa);
    }

    public ResultadoConversaoCompraInsumo ConverterCompra(
        Guid formaCompraId,
        decimal quantidadeEmbalagens)
    {
        var formaCompra = ObterFormaCompra(formaCompraId);

        if (!Ativo)
        {
            throw new InvalidOperationException("Um insumo inativo não pode ser usado em novas conversões.");
        }

        if (!formaCompra.Ativa)
        {
            throw new InvalidOperationException("Uma forma de compra inativa não pode ser usada em novas conversões.");
        }

        if (quantidadeEmbalagens <= 0 || decimal.Truncate(quantidadeEmbalagens) != quantidadeEmbalagens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeEmbalagens),
                "A quantidade de embalagens deve ser um inteiro positivo.");
        }

        decimal quantidadeBase;
        try
        {
            quantidadeBase = checked(
                quantidadeEmbalagens * formaCompra.QuantidadeEquivalentePorEmbalagem.ValorBase);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeEmbalagens),
                "A quantidade convertida excede o limite suportado.");
        }

        var quantidadeNormalizada = QuantidadeEstoque.Criar(quantidadeBase, UnidadeBase);
        return new ResultadoConversaoCompraInsumo(
            Id,
            formaCompra.Id,
            formaCompra.Nome,
            quantidadeEmbalagens,
            formaCompra.QuantidadeEquivalentePorEmbalagem,
            quantidadeNormalizada);
    }

    private static string NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O nome do insumo é obrigatório.", nameof(nome));
        }

        var normalized = NormalizarEspacos(nome);
        if (normalized.Length > NomeMaxLength)
        {
            throw new ArgumentException(
                $"O nome do insumo não pode exceder {NomeMaxLength} caracteres.",
                nameof(nome));
        }

        return normalized;
    }

    private static void ValidarTipo(TipoInsumoEstoque tipo)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de insumo inválido.");
        }
    }

    private static void ValidarUnidadeBase(UnidadeFisicaEstoque unidadeBase)
    {
        if (unidadeBase is not UnidadeFisicaEstoque.Grama
            and not UnidadeFisicaEstoque.Mililitro
            and not UnidadeFisicaEstoque.Unidade)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unidadeBase),
                unidadeBase,
                "A unidade-base deve ser grama, mililitro ou unidade.");
        }
    }

    private static void ValidarEstoqueMinimo(
        QuantidadeEstoque? estoqueMinimo,
        UnidadeFisicaEstoque unidadeBase)
    {
        if (estoqueMinimo is not null
            && estoqueMinimo.UnidadeBase != unidadeBase)
        {
            throw new ArgumentException(
                "O estoque mínimo deve pertencer à dimensão e à unidade-base do insumo.",
                nameof(estoqueMinimo));
        }
    }

    private static void ValidarConfiguracaoLote(bool controlaLote, bool validadeObrigatoria)
    {
        if (validadeObrigatoria && !controlaLote)
        {
            throw new ArgumentException(
                "A validade obrigatória exige controle por lote.",
                nameof(validadeObrigatoria));
        }
    }

    private void ValidarFatorCompativel(QuantidadeEstoque fator, string paramName)
    {
        if (fator.UnidadeBase != UnidadeBase)
        {
            throw new ArgumentException(
                "O fator da forma de compra deve pertencer à dimensão e à unidade-base do insumo.",
                paramName);
        }
    }

    private void ValidarNomeFormaUnico(string nomeNormalizado, Guid? excetoFormaId = null)
    {
        if (_formasCompra.Any(forma =>
                forma.Id != excetoFormaId
                && string.Equals(forma.NomeNormalizado, nomeNormalizado, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Já existe uma forma de compra com esse nome normalizado neste insumo.");
        }
    }

    private FormaCompraInsumo ObterFormaCompra(Guid formaCompraId) =>
        _formasCompra.SingleOrDefault(forma => forma.Id == formaCompraId)
        ?? throw new ArgumentException("A forma de compra não pertence a este insumo.", nameof(formaCompraId));

    private static string NormalizarEspacos(string value)
    {
        var normalized = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(character);
        }

        return normalized.ToString();
    }
}
