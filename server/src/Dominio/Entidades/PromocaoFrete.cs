namespace Xaquiamuni.Dominio.Entidades;

public enum TipoDescontoFrete { Percentual = 1, ValorFixo = 2, FreteGratis = 3 }

[Flags]
public enum ModalidadesPromocaoFrete { EntregaPropria = 1, CorreiosManual = 2 }

public sealed record ConfiguracaoPromocaoFrete(string Nome, TipoDescontoFrete Tipo, long Valor,
    ModalidadesPromocaoFrete Modalidades, long SubtotalMinimoCentavos, DateTimeOffset? InicioUtc, DateTimeOffset? FimUtc);

public sealed class PromocaoFrete
{
    public const int LimiteNome = 120;
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = null!;
    public TipoDescontoFrete Tipo { get; private set; }
    public long Valor { get; private set; }
    public ModalidadesPromocaoFrete Modalidades { get; private set; }
    public long SubtotalMinimoCentavos { get; private set; }
    public DateTimeOffset? InicioUtc { get; private set; }
    public DateTimeOffset? FimUtc { get; private set; }
    public bool Ativa { get; private set; }
    public int Versao { get; private set; }
    public Guid CriadaPorId { get; private set; }
    public DateTimeOffset CriadaEmUtc { get; private set; }
    public Guid AlteradaPorId { get; private set; }
    public DateTimeOffset AlteradaEmUtc { get; private set; }

    private PromocaoFrete() { }

    public static PromocaoFrete Criar(ConfiguracaoPromocaoFrete dados, Guid autor, DateTimeOffset agora)
    {
        var campanha = new PromocaoFrete { Id = Guid.NewGuid(), CriadaPorId = autor, CriadaEmUtc = agora.ToUniversalTime() };
        campanha.Editar(dados, autor, agora);
        return campanha;
    }

    public void Editar(ConfiguracaoPromocaoFrete dados, Guid autor, DateTimeOffset agora)
    {
        var nome = dados.Nome.Trim();
        if (nome.Length is 0 or > LimiteNome)
            throw new ArgumentException("Informe um nome de até 120 caracteres.", "nome");
        var valorValido = dados.Tipo switch
        {
            TipoDescontoFrete.Percentual => dados.Valor is >= 1 and <= 10_000,
            TipoDescontoFrete.ValorFixo => dados.Valor > 0,
            TipoDescontoFrete.FreteGratis => dados.Valor == 0,
            _ => false
        };
        if (!valorValido)
            throw new ArgumentException("Informe um desconto válido para o tipo selecionado.", "valor");
        const ModalidadesPromocaoFrete aceitas = ModalidadesPromocaoFrete.EntregaPropria | ModalidadesPromocaoFrete.CorreiosManual;
        if (dados.Modalidades == 0 || (dados.Modalidades & ~aceitas) != 0)
            throw new ArgumentException("Selecione entrega própria, Correios manual ou ambas.", "modalidades");
        if (dados.SubtotalMinimoCentavos < 0)
            throw new ArgumentException("O subtotal mínimo deve ser zero ou maior.", "subtotalMinimoCentavos");
        if (dados.InicioUtc >= dados.FimUtc)
            throw new ArgumentException("O término deve ser posterior ao início.", "fimUtc");

        RegistrarAlteracao(autor, agora);
        Nome = nome;
        Tipo = dados.Tipo;
        Valor = dados.Valor;
        Modalidades = dados.Modalidades;
        SubtotalMinimoCentavos = dados.SubtotalMinimoCentavos;
        InicioUtc = dados.InicioUtc?.ToUniversalTime();
        FimUtc = dados.FimUtc?.ToUniversalTime();
    }

    public void AlterarStatus(bool ativa, Guid autor, DateTimeOffset agora)
    {
        RegistrarAlteracao(autor, agora);
        Ativa = ativa;
    }

    public bool VigenteEm(DateTimeOffset instante) =>
        Ativa && (InicioUtc is null || InicioUtc <= instante) && (FimUtc is null || instante < FimUtc);

    private void RegistrarAlteracao(Guid autor, DateTimeOffset agora)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(autor, Guid.Empty);
        Versao = checked(Versao + 1);
        AlteradaPorId = autor;
        AlteradaEmUtc = agora.ToUniversalTime();
    }
}
