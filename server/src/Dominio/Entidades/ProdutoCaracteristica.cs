using System.Text;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoCaracteristica
{
    public const int NomeMaxLength = 100;

    private readonly List<ProdutoCaracteristicaValor> _valores = [];

    private ProdutoCaracteristica()
    {
    }

    private ProdutoCaracteristica(
        Guid id,
        Guid produtoId,
        string nome,
        string nomeNormalizado,
        TipoMedidaFisica? tipoMedidaFisica,
        Produto produto)
    {
        Id = id;
        ProdutoId = produtoId;
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        TipoMedidaFisica = tipoMedidaFisica;
        Produto = produto;
    }

    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string NomeNormalizado { get; private set; } = null!;
    public TipoMedidaFisica? TipoMedidaFisica { get; private set; }
    public Produto Produto { get; private set; } = null!;
    public IReadOnlyCollection<ProdutoCaracteristicaValor> Valores => _valores.AsReadOnly();

    internal static ProdutoCaracteristica Criar(
        Produto produto,
        string? nome,
        TipoMedidaFisica? tipoMedidaFisica)
    {
        ArgumentNullException.ThrowIfNull(produto);
        var nomeNormalizado = NormalizarNome(nome);
        if (tipoMedidaFisica is not null && !Enum.IsDefined(tipoMedidaFisica.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(tipoMedidaFisica));
        }

        return new ProdutoCaracteristica(
            Guid.NewGuid(),
            produto.Id,
            nomeNormalizado,
            nomeNormalizado.ToUpperInvariant(),
            tipoMedidaFisica,
            produto);
    }

    public ProdutoCaracteristicaValor AdicionarValor(string? nome)
    {
        if (TipoMedidaFisica is not null)
        {
            throw new InvalidOperationException(
                "Valores de características físicas devem ser definidos por medida numérica.");
        }

        var normalizedName = NormalizarNome(nome);
        var normalizada = normalizedName.ToUpperInvariant();
        return AdicionarValorNormalizado(normalizedName, normalizada, null);
    }

    public ProdutoCaracteristicaValor AdicionarValorMedida(decimal quantidade, string? unidade)
    {
        if (TipoMedidaFisica is null)
        {
            throw new InvalidOperationException(
                "Esta característica não está vinculada a uma medida física.");
        }

        var canonicalValue = TipoMedidaFisica switch
        {
            Xaquiamuni.Dominio.Entidades.TipoMedidaFisica.PesoLiquido =>
                MedidaFisicaCatalogo.ConverterPesoParaGramas(quantidade, unidade),
            Xaquiamuni.Dominio.Entidades.TipoMedidaFisica.VolumeLiquido =>
                MedidaFisicaCatalogo.ConverterVolumeParaMililitros(quantidade, unidade),
            _ => throw new ArgumentOutOfRangeException(nameof(TipoMedidaFisica))
        };
        var normalizedName = MedidaFisicaCatalogo.FormatarValor(TipoMedidaFisica.Value, canonicalValue);
        return AdicionarValorNormalizado(
            normalizedName,
            normalizedName.ToUpperInvariant(),
            canonicalValue);
    }

    public void AtualizarDados(string? nome, TipoMedidaFisica? tipoMedidaFisica)
    {
        var normalizedName = NormalizarNome(nome);
        if (tipoMedidaFisica is not null && !Enum.IsDefined(tipoMedidaFisica.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(tipoMedidaFisica));
        }

        if (tipoMedidaFisica != TipoMedidaFisica && _valores.Count > 0)
        {
            throw new InvalidOperationException(
                "O tipo de uma característica com valores existentes não pode ser alterado.");
        }

        Nome = normalizedName;
        NomeNormalizado = normalizedName.ToUpperInvariant();
        TipoMedidaFisica = tipoMedidaFisica;
    }

    public void AtualizarValor(Guid valorId, string? nome, decimal? valorMedidaCanonico)
    {
        var valor = _valores.SingleOrDefault(item => item.Id == valorId)
            ?? throw new ArgumentException("O valor não pertence a esta característica.", nameof(valorId));

        string nomeNormalizado;
        decimal? medidaNormalizada;
        if (TipoMedidaFisica is null)
        {
            if (valorMedidaCanonico is not null)
            {
                throw new ArgumentException(
                    "Uma característica genérica não aceita valor numérico físico.",
                    nameof(valorMedidaCanonico));
            }

            nomeNormalizado = NormalizarNome(nome);
            medidaNormalizada = null;
        }
        else
        {
            if (nome is not null)
            {
                throw new ArgumentException(
                    "O nome de um valor físico é derivado da medida canônica.",
                    nameof(nome));
            }

            medidaNormalizada = TipoMedidaFisica switch
            {
                Xaquiamuni.Dominio.Entidades.TipoMedidaFisica.PesoLiquido =>
                    MedidaFisicaCatalogo.ValidarPesoCanonico(valorMedidaCanonico, nameof(valorMedidaCanonico)),
                Xaquiamuni.Dominio.Entidades.TipoMedidaFisica.VolumeLiquido =>
                    MedidaFisicaCatalogo.ValidarVolumeCanonico(valorMedidaCanonico, nameof(valorMedidaCanonico)),
                _ => throw new ArgumentOutOfRangeException(nameof(TipoMedidaFisica))
            };
            if (medidaNormalizada is null)
            {
                throw new ArgumentException("A medida canônica é obrigatória.", nameof(valorMedidaCanonico));
            }

            nomeNormalizado = MedidaFisicaCatalogo.FormatarValor(TipoMedidaFisica.Value, medidaNormalizada.Value);
        }

        var chaveNormalizada = nomeNormalizado.ToUpperInvariant();
        if (_valores.Any(item => item.Id != valorId
            && string.Equals(item.NomeNormalizado, chaveNormalizada, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Já existe um valor equivalente nesta característica.");
        }

        valor.AtualizarDados(nomeNormalizado, chaveNormalizada, medidaNormalizada);
    }

    internal void RemoverValor(Guid valorId)
    {
        var valor = _valores.SingleOrDefault(item => item.Id == valorId)
            ?? throw new ArgumentException("O valor não pertence a esta característica.", nameof(valorId));
        _valores.Remove(valor);
    }

    internal void LimparValores() => _valores.Clear();

    private ProdutoCaracteristicaValor AdicionarValorNormalizado(
        string nome,
        string nomeNormalizado,
        decimal? valorMedidaCanonico)
    {
        if (_valores.Any(value => string.Equals(
                value.NomeNormalizado,
                nomeNormalizado,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Já existe um valor equivalente nesta característica.");
        }

        var value = ProdutoCaracteristicaValor.Criar(
            this,
            nome,
            nomeNormalizado,
            valorMedidaCanonico);
        _valores.Add(value);
        return value;
    }

    internal static string NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O nome da característica ou do valor é obrigatório.", nameof(nome));
        }

        var normalized = NormalizarEspacos(nome);
        if (normalized.Length > NomeMaxLength)
        {
            throw new ArgumentException(
                $"O nome da característica ou do valor não pode exceder {NomeMaxLength} caracteres.",
                nameof(nome));
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
