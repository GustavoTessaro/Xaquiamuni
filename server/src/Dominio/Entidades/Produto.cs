using System.Security.Cryptography;
using System.Text;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class Produto
{
    public const int NomeMaxLength = 200;
    public const int DescricaoMaxLength = 4000;
    public const string IdentificacaoVariantePadrao = "Padrão";

    private readonly List<ProdutoVariante> _variantes = [];
    private readonly List<ProdutoCaracteristica> _caracteristicas = [];

    private Produto()
    {
    }

    private Produto(Guid id, string nome, string? descricao, Categoria categoria)
    {
        Id = id;
        Nome = nome;
        Descricao = descricao;
        CategoriaId = categoria.Id;
        Categoria = categoria;
    }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public Guid CategoriaId { get; private set; }
    public Categoria Categoria { get; private set; } = null!;
    public bool Publicado { get; private set; }
    public IReadOnlyCollection<ProdutoVariante> Variantes => _variantes.AsReadOnly();
    public IReadOnlyCollection<ProdutoCaracteristica> Caracteristicas => _caracteristicas.AsReadOnly();

    public static Produto Criar(
        string? nome,
        string? descricao,
        Categoria? categoria,
        IEnumerable<ProdutoVariante>? variantes)
    {
        var normalizedName = NormalizarNome(nome);
        var normalizedDescription = NormalizarDescricao(descricao);
        ValidarCategoria(categoria);
        var variants = variantes?.ToArray()
            ?? throw new ArgumentNullException(nameof(variantes));

        if (variants.Length == 0)
        {
            throw new ArgumentException("O produto deve possuir ao menos uma variante.", nameof(variantes));
        }

        if (variants.Any(variant => variant is null))
        {
            throw new ArgumentException("As variantes do produto não podem ser nulas.", nameof(variantes));
        }

        var defaults = variants.Count(variant => variant.Padrao);
        if (defaults != 1)
        {
            throw new ArgumentException(
                "O produto deve possuir exatamente uma variante padrão.",
                nameof(variantes));
        }

        if (variants.Any(variant => !variant.Ativa && variant.Padrao))
        {
            throw new ArgumentException("A variante padrão deve estar ativa.", nameof(variantes));
        }

        if (variants.Any(variant => variant.ProdutoId != Guid.Empty))
        {
            throw new ArgumentException(
                "Uma variante já associada a um produto não pode ser reutilizada.",
                nameof(variantes));
        }

        if (variants.Select(variant => variant.IdentificacaoNormalizada)
            .Distinct(StringComparer.Ordinal)
            .Count() != variants.Length)
        {
            throw new InvalidOperationException(
                "Já existe uma variante deste produto com essa identificação.");
        }

        var produto = new Produto(Guid.NewGuid(), normalizedName, normalizedDescription, categoria!);
        foreach (var variant in variants)
        {
            produto.AssociarVariante(variant);
        }

        return produto;
    }

    public static Produto CriarSimples(
        string? nome,
        string? descricao,
        Categoria? categoria,
        string? unidadeVenda,
        decimal precoVenda,
        decimal? pesoLiquidoGramas = null,
        decimal? volumeLiquidoMililitros = null)
    {
        var defaultVariant = ProdutoVariante.Criar(
            IdentificacaoVariantePadrao,
            unidadeVenda,
            precoVenda,
            ativa: true,
            padrao: true,
            pesoLiquidoGramas,
            volumeLiquidoMililitros);
        return Criar(nome, descricao, categoria, [defaultVariant]);
    }

    public ProdutoCaracteristica AdicionarCaracteristica(
        string? nome,
        TipoMedidaFisica? tipoMedidaFisica = null)
    {
        var normalizedName = ProdutoCaracteristica.NormalizarNome(nome);
        var normalizedKey = normalizedName.ToUpperInvariant();
        if (_caracteristicas.Any(item => string.Equals(
                item.NomeNormalizado,
                normalizedKey,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Já existe uma característica equivalente neste produto.");
        }

        if (tipoMedidaFisica is not null
            && _caracteristicas.Any(item => item.TipoMedidaFisica == tipoMedidaFisica))
        {
            throw new InvalidOperationException(
                "O produto já possui uma característica vinculada a esta medida física.");
        }

        var caracteristica = ProdutoCaracteristica.Criar(this, normalizedName, tipoMedidaFisica);
        _caracteristicas.Add(caracteristica);
        return caracteristica;
    }

    public void AtualizarCaracteristica(
        Guid caracteristicaId,
        string? nome,
        TipoMedidaFisica? tipoMedidaFisica)
    {
        var caracteristica = ObterCaracteristica(caracteristicaId);
        var normalizedName = ProdutoCaracteristica.NormalizarNome(nome).ToUpperInvariant();
        if (_caracteristicas.Any(item => item.Id != caracteristicaId
            && string.Equals(item.NomeNormalizado, normalizedName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Já existe uma característica equivalente neste produto.");
        }

        caracteristica.AtualizarDados(nome, tipoMedidaFisica);
    }

    public void RemoverCaracteristica(Guid caracteristicaId)
    {
        var caracteristica = ObterCaracteristica(caracteristicaId);
        if (_variantes.Any(variante => variante.CaracteristicaValores.Any(associacao =>
                associacao.ProdutoCaracteristicaId == caracteristicaId)))
        {
            throw new InvalidOperationException(
                "A característica não pode ser removida enquanto estiver associada a uma variante.");
        }

        caracteristica.LimparValores();
        _caracteristicas.Remove(caracteristica);
    }

    public void AtualizarValorCaracteristica(
        Guid caracteristicaId,
        Guid valorId,
        string? nome,
        decimal? valorMedidaCanonico) =>
        ObterCaracteristica(caracteristicaId).AtualizarValor(valorId, nome, valorMedidaCanonico);

    public void RemoverValorCaracteristica(Guid caracteristicaId, Guid valorId)
    {
        _ = ObterCaracteristica(caracteristicaId).Valores.SingleOrDefault(item => item.Id == valorId)
            ?? throw new ArgumentException(
                "O valor não pertence a esta característica.",
                nameof(valorId));
        if (_variantes.Any(variante => variante.CaracteristicaValores.Any(associacao =>
                associacao.ProdutoCaracteristicaValorId == valorId)))
        {
            throw new InvalidOperationException(
                "O valor não pode ser removido enquanto estiver associado a uma variante.");
        }

        ObterCaracteristica(caracteristicaId).RemoverValor(valorId);
    }

    public void AssociarValorCaracteristica(
        Guid varianteId,
        Guid caracteristicaId,
        Guid valorId)
    {
        var variante = ObterVariante(varianteId);
        var caracteristica = _caracteristicas.SingleOrDefault(item => item.Id == caracteristicaId)
            ?? throw new ArgumentException(
                "A característica não pertence a este produto.",
                nameof(caracteristicaId));
        var valor = caracteristica.Valores.SingleOrDefault(item => item.Id == valorId)
            ?? throw new ArgumentException(
                "O valor não pertence a esta característica e produto.",
                nameof(valorId));
        var associacao = ProdutoVarianteCaracteristicaValor.Criar(variante, valor);
        var combinacao = CalcularCombinacaoHash(variante.CaracteristicaValores.Append(associacao));

        if (combinacao is not null && _variantes.Any(item =>
                item.Id != varianteId && item.TemCombinacao(combinacao)))
        {
            throw new InvalidOperationException(
                "Já existe uma variante deste produto com a mesma combinação de características.");
        }

        variante.AssociarCaracteristicaValor(associacao, combinacao);
    }

    public void SubstituirValoresCaracteristica(
        Guid varianteId,
        IEnumerable<(Guid CaracteristicaId, Guid ValorId)> valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        var variante = ObterVariante(varianteId);
        var associações = valores.Select(par =>
        {
            var caracteristica = ObterCaracteristica(par.CaracteristicaId);
            var valor = caracteristica.Valores.SingleOrDefault(item => item.Id == par.ValorId)
                ?? throw new ArgumentException(
                    "O valor não pertence a esta característica e produto.",
                    nameof(valores));
            return ProdutoVarianteCaracteristicaValor.Criar(variante, valor);
        }).ToArray();
        var combinacao = CalcularCombinacaoHash(associações);

        if (combinacao is not null && _variantes.Any(item =>
                item.Id != varianteId && item.TemCombinacao(combinacao)))
        {
            throw new InvalidOperationException(
                "Já existe uma variante deste produto com a mesma combinação de características.");
        }

        variante.SubstituirCaracteristicaValores(associações, combinacao);
    }

    public void AtualizarDados(string? nome, string? descricao)
    {
        var normalizedName = NormalizarNome(nome);
        var normalizedDescription = NormalizarDescricao(descricao);

        Nome = normalizedName;
        Descricao = normalizedDescription;
    }

    public void AlterarCategoria(Categoria? categoria)
    {
        ValidarCategoria(categoria);
        Categoria = categoria!;
        CategoriaId = categoria!.Id;
    }

    public void AdicionarVariante(ProdutoVariante? variante)
    {
        ArgumentNullException.ThrowIfNull(variante);
        if (variante.Padrao)
        {
            throw new ArgumentException(
                "A variante padrão é definida pelo produto e não pode ser adicionada como padrão.",
                nameof(variante));
        }

        if (variante.ProdutoId != Guid.Empty)
        {
            throw new ArgumentException(
                "Uma variante já associada a um produto não pode ser reutilizada.",
                nameof(variante));
        }

        if (_variantes.Any(existing =>
                string.Equals(
                    existing.IdentificacaoNormalizada,
                    variante.IdentificacaoNormalizada,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Já existe uma variante deste produto com essa identificação.");
        }

        AssociarVariante(variante);
    }

    public void AtualizarVariante(
        Guid varianteId,
        string? identificacao,
        string? unidadeVenda,
        decimal precoVenda)
    {
        var variante = ObterVariante(varianteId);
        var identification = ProdutoVariante.NormalizarIdentificacao(identificacao);
        var normalizedIdentification = identification.ToUpperInvariant();
        if (_variantes.Any(existing =>
                existing.Id != varianteId
                && string.Equals(
                    existing.IdentificacaoNormalizada,
                    normalizedIdentification,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Já existe uma variante deste produto com essa identificação.");
        }

        variante.AtualizarDados(identificacao, unidadeVenda, precoVenda);
    }

    public void DefinirVariantePadrao(Guid varianteId)
    {
        var variante = ObterVariante(varianteId);
        if (!variante.Ativa)
        {
            throw new InvalidOperationException("Uma variante inativa não pode ser padrão.");
        }

        if (variante.Padrao)
        {
            return;
        }

        var atual = _variantes.Single(item => item.Padrao);
        atual.DefinirPadrao(false);
        variante.DefinirPadrao(true);
    }

    public void AtivarVariante(Guid varianteId) =>
        ObterVariante(varianteId).DefinirAtiva(true);

    public void DesativarVariante(Guid varianteId, Guid? novoPadraoId = null)
    {
        var variante = ObterVariante(varianteId);
        if (!variante.Ativa)
        {
            return;
        }

        if (variante.Padrao)
        {
            if (novoPadraoId is null || novoPadraoId == variante.Id)
            {
                throw new InvalidOperationException(
                    "Para desativar a variante padrão, indique outra variante ativa como padrão.");
            }

            var novoPadrao = ObterVariante(novoPadraoId.Value);
            if (!novoPadrao.Ativa)
            {
                throw new InvalidOperationException(
                    "A nova variante padrão deve estar ativa antes da troca.");
            }

            variante.DefinirPadrao(false);
            novoPadrao.DefinirPadrao(true);
        }
        else if (novoPadraoId is not null)
        {
            throw new ArgumentException(
                "Só é possível trocar a variante padrão ao desativar a variante padrão atual.",
                nameof(novoPadraoId));
        }

        if (_variantes.Count(item => item.Ativa) <= 1)
        {
            throw new InvalidOperationException("A última variante ativa não pode ser desativada.");
        }

        variante.DefinirAtiva(false);
    }

    public void Publicar()
    {
        if (!Categoria.Ativa)
        {
            throw new InvalidOperationException(
                "Um produto associado a uma categoria inativa não pode ser publicado.");
        }

        if (_variantes.Count == 0
            || !_variantes.Any(variant => variant.Padrao && variant.Ativa)
            || !_variantes.Any(variant => variant.Ativa))
        {
            throw new InvalidOperationException(
                "Um produto publicado deve possuir uma variante padrão ativa.");
        }

        Publicado = true;
    }

    public void Despublicar()
    {
        Publicado = false;
    }

    private ProdutoVariante ObterVariante(Guid id) =>
        _variantes.SingleOrDefault(variant => variant.Id == id)
        ?? throw new ArgumentException("A variante não pertence a este produto.", nameof(id));

    private ProdutoCaracteristica ObterCaracteristica(Guid id) =>
        _caracteristicas.SingleOrDefault(item => item.Id == id)
        ?? throw new ArgumentException("A característica não pertence a este produto.", nameof(id));

    private void AssociarVariante(ProdutoVariante variante)
    {
        variante.AssociarA(this);
        _variantes.Add(variante);
    }

    private static byte[]? CalcularCombinacaoHash(
        IEnumerable<ProdutoVarianteCaracteristicaValor> valores)
    {
        var canonicalValues = valores
            .OrderBy(item => item.ProdutoCaracteristicaId)
            .Select(item => $"{item.ProdutoCaracteristicaId:N}:{item.ProdutoCaracteristicaValorId:N}")
            .ToArray();
        return canonicalValues.Length == 0
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', canonicalValues)));
    }

    private static void ValidarCategoria(Categoria? categoria)
    {
        ArgumentNullException.ThrowIfNull(categoria);
        if (!categoria.Ativa)
        {
            throw new ArgumentException(
                "Uma categoria inativa não pode ser atribuída a um novo produto.",
                nameof(categoria));
        }
    }

    private static string NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O nome do produto é obrigatório.", nameof(nome));
        }

        var normalized = NormalizarEspacos(nome);
        if (normalized.Length > NomeMaxLength)
        {
            throw new ArgumentException(
                $"O nome do produto não pode exceder {NomeMaxLength} caracteres.",
                nameof(nome));
        }

        return normalized;
    }

    private static string? NormalizarDescricao(string? descricao)
    {
        if (string.IsNullOrWhiteSpace(descricao))
        {
            return null;
        }

        var normalized = descricao.Trim();
        if (normalized.Length > DescricaoMaxLength)
        {
            throw new ArgumentException(
                $"A descrição do produto não pode exceder {DescricaoMaxLength} caracteres.",
                nameof(descricao));
        }

        return normalized;
    }

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
