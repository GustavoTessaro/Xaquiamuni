using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Dominio.Entidades;

public enum SituacaoPedido : short
{
    AguardandoPagamento = 1,
    PagamentoConfirmado = 2,
    EmPreparacao = 3,
    ProntoParaRetirada = 4,
    Entregue = 5,
    Cancelado = 6,
    Expirado = 7,
}

public enum ModalidadeEntregaPedido : short
{
    RetiradaLocal = 1,
    EntregaPropria = 2,
}

public sealed record EnderecoEntregaPedido(
    string Cep, string Uf, string Cidade, string Bairro, string Logradouro, string Numero,
    string? Complemento, string CodigoIbge);

public sealed record DadosEntregaPedido(
    EnderecoEntregaPedido? Destino = null, Guid? CidadeId = null,
    string? EnderecoRetirada = null, string? InstrucoesRetirada = null);

public sealed class Pedido
{
    public const int MotivoCancelamentoMaxLength = 500;

    private readonly List<ItemPedido> _itens = [];

    private Pedido()
    {
    }

    public Guid Id { get; private set; }
    public Guid ClienteId { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset PrazoPagamentoEmUtc { get; private set; }
    public SituacaoPedido Situacao { get; private set; }
    public ModalidadeEntregaPedido ModalidadeEntrega { get; private set; }
    public long TotalCentavos { get; private set; }
    public long FreteCentavos { get; private set; }
    public long SubtotalProdutosCentavos { get; private set; }
    public DadosEntregaPedido? Entrega { get; private set; }
    public string? HashOfertaEntrega { get; private set; }
    public IReadOnlyList<ItemPedido> Itens => _itens.AsReadOnly();
    public DateTimeOffset? PagamentoConfirmadoEmUtc { get; private set; }
    public Guid? PagamentoConfirmadoPorId { get; private set; }
    public DateTimeOffset? PreparacaoIniciadaEmUtc { get; private set; }
    public Guid? PreparacaoIniciadaPorId { get; private set; }
    public DateTimeOffset? RetiradaAutorizadaEmUtc { get; private set; }
    public Guid? RetiradaAutorizadaPorId { get; private set; }
    public DateTimeOffset? EncerradoEmUtc { get; private set; }
    public Guid? EncerradoPorId { get; private set; }
    public string? MotivoCancelamento { get; private set; }

    public static Pedido Criar(Guid clienteId, DateTimeOffset criadoEmUtc, IEnumerable<DadosItemPedido> itens,
        Guid? pedidoId = null, DadosEntregaPedido? entrega = null, long freteCentavos = 0)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(clienteId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(itens);
        ArgumentOutOfRangeException.ThrowIfEqual(pedidoId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(freteCentavos);
        if ((entrega?.Destino is null) != (entrega?.CidadeId is null)
            || (entrega?.Destino is null && freteCentavos != 0))
            throw new ArgumentException("Dados de entrega inconsistentes.", nameof(entrega));
        ValidarUtc(criadoEmUtc, nameof(criadoEmUtc));
        if (criadoEmUtc > DateTimeOffset.MaxValue.Subtract(PrazoPagamentoCompra.Duracao))
        {
            throw new ArgumentOutOfRangeException(nameof(criadoEmUtc), "O prazo de pagamento ultrapassa o intervalo suportado.");
        }

        var pedido = new Pedido
        {
            Id = pedidoId ?? Guid.NewGuid(),
            ClienteId = clienteId,
            CriadoEmUtc = criadoEmUtc,
            PrazoPagamentoEmUtc = criadoEmUtc.Add(PrazoPagamentoCompra.Duracao),
            Situacao = SituacaoPedido.AguardandoPagamento,
            ModalidadeEntrega = entrega?.Destino is null
                ? ModalidadeEntregaPedido.RetiradaLocal : ModalidadeEntregaPedido.EntregaPropria,
            Entrega = entrega,
            FreteCentavos = freteCentavos,
        };
        pedido._itens.AddRange(itens.Select(item => ItemPedido.Criar(pedido.Id, item)));
        if (pedido._itens.Count == 0)
        {
            throw new ArgumentException("O pedido deve possuir ao menos um item.", nameof(itens));
        }

        if (pedido._itens.Select(item => item.ProdutoVarianteId).Distinct().Count() != pedido._itens.Count)
        {
            throw new ArgumentException("Cada variante deve constar em um único item.", nameof(itens));
        }

        try
        {
            pedido.SubtotalProdutosCentavos = pedido._itens.Sum(item => item.SubtotalCentavos);
            pedido.TotalCentavos = checked(pedido.SubtotalProdutosCentavos + freteCentavos);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(itens), "O valor em centavos ultrapassa o limite suportado.");
        }
        return pedido;
    }

    // O futuro caso de uso deve validar autorização e evidência bancária.
    public void ConfirmarPagamento(Guid responsavelId, DateTimeOffset instanteUtc)
    {
        ExigirSituacao(SituacaoPedido.AguardandoPagamento);
        ValidarAcao(responsavelId, instanteUtc);
        ExigirPrazoVigente(instanteUtc);
        PagamentoConfirmadoEmUtc = instanteUtc;
        PagamentoConfirmadoPorId = responsavelId;
        Situacao = SituacaoPedido.PagamentoConfirmado;
    }

    public void IniciarPreparacao(Guid responsavelId, DateTimeOffset instanteUtc)
    {
        ExigirSituacao(SituacaoPedido.PagamentoConfirmado);
        ValidarAcao(responsavelId, instanteUtc);
        PreparacaoIniciadaEmUtc = instanteUtc;
        PreparacaoIniciadaPorId = responsavelId;
        Situacao = SituacaoPedido.EmPreparacao;
    }

    public void AutorizarRetirada(Guid responsavelId, DateTimeOffset instanteUtc)
    {
        ExigirSituacao(SituacaoPedido.EmPreparacao);
        ValidarAcao(responsavelId, instanteUtc);
        RetiradaAutorizadaEmUtc = instanteUtc;
        RetiradaAutorizadaPorId = responsavelId;
        Situacao = SituacaoPedido.ProntoParaRetirada;
    }

    // A futura aplicação deve registrar a entrega e a baixa física na mesma transação.
    public void ConfirmarEntrega(Guid responsavelId, DateTimeOffset instanteUtc)
    {
        ExigirSituacao(SituacaoPedido.ProntoParaRetirada);
        ValidarAcao(responsavelId, instanteUtc);
        Encerrar(SituacaoPedido.Entregue, instanteUtc, responsavelId);
    }

    public void Cancelar(Guid responsavelId, DateTimeOffset instanteUtc, string? motivo)
    {
        if (EncerradoEmUtc is not null)
        {
            throw new InvalidOperationException("Um pedido encerrado não pode ser cancelado.");
        }
        ValidarAcao(responsavelId, instanteUtc);
        if (Situacao == SituacaoPedido.AguardandoPagamento)
        {
            ExigirPrazoVigente(instanteUtc);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        var normalizado = motivo.Trim();
        if (normalizado.Length > MotivoCancelamentoMaxLength)
        {
            throw new ArgumentException("O motivo de cancelamento excede o limite permitido.", nameof(motivo));
        }
        MotivoCancelamento = normalizado;
        Encerrar(SituacaoPedido.Cancelado, instanteUtc, responsavelId);
    }

    public void Expirar(DateTimeOffset instanteUtc)
    {
        ExigirSituacao(SituacaoPedido.AguardandoPagamento);
        ValidarInstante(instanteUtc);
        if (instanteUtc < PrazoPagamentoEmUtc)
        {
            throw new InvalidOperationException("O prazo de pagamento ainda está vigente.");
        }
        Encerrar(SituacaoPedido.Expirado, instanteUtc);
    }

    private void ExigirSituacao(SituacaoPedido esperada)
    {
        if (Situacao != esperada)
        {
            throw new InvalidOperationException("A transição não é permitida na situação atual do pedido.");
        }
    }

    private void ExigirPrazoVigente(DateTimeOffset instanteUtc)
    {
        if (instanteUtc >= PrazoPagamentoEmUtc)
        {
            throw new InvalidOperationException("O prazo de pagamento terminou.");
        }
    }

    private void ValidarAcao(Guid responsavelId, DateTimeOffset instanteUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(responsavelId, Guid.Empty);
        ValidarInstante(instanteUtc);
    }

    private void ValidarInstante(DateTimeOffset instanteUtc)
    {
        ValidarUtc(instanteUtc, nameof(instanteUtc));
        var anterior = RetiradaAutorizadaEmUtc ?? PreparacaoIniciadaEmUtc ?? PagamentoConfirmadoEmUtc ?? CriadoEmUtc;
        if (instanteUtc < anterior)
        {
            throw new ArgumentOutOfRangeException(nameof(instanteUtc), "O instante não pode ser anterior à última transição.");
        }
    }

    private static void ValidarUtc(DateTimeOffset instanteUtc, string parametro)
    {
        if (instanteUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante deve estar em UTC.", parametro);
        }
    }

    private void Encerrar(SituacaoPedido situacao, DateTimeOffset instanteUtc, Guid? responsavelId = null)
    {
        Situacao = situacao;
        EncerradoEmUtc = instanteUtc;
        EncerradoPorId = responsavelId;
    }
}
