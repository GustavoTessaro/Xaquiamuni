using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class PedidoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly Guid Responsavel = Guid.NewGuid();

    [Fact]
    public void Criar_UsaRetiradaLocalEPrazoCompartilhadoComReservas()
    {
        var dados = CriarItem();
        var pedido = Pedido.Criar(Cliente, Agora, [dados]);
        var item = Assert.Single(pedido.Itens);

        Assert.NotEqual(Guid.Empty, pedido.Id);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(Cliente, pedido.ClienteId);
        Assert.Equal(pedido.Id, item.PedidoId);
        Assert.Equal(dados.ProdutoId, item.ProdutoId);
        Assert.Equal(dados.ProdutoVarianteId, item.ProdutoVarianteId);
        Assert.Equal(SituacaoPedido.AguardandoPagamento, pedido.Situacao);
        Assert.Equal(ModalidadeEntregaPedido.RetiradaLocal, pedido.ModalidadeEntrega);
        Assert.Equal(Agora, pedido.CriadoEmUtc);
        Assert.Equal(Agora.AddHours(24), pedido.PrazoPagamentoEmUtc);
        Assert.Equal(Agora.Add(ReservaProdutoAcabado.Prazo), pedido.PrazoPagamentoEmUtc);
        Assert.Equal(TimeSpan.Zero, pedido.PrazoPagamentoEmUtc.Offset);
        Assert.Equal(398, pedido.TotalCentavos);
        Assert.Null(pedido.EncerradoEmUtc);
    }

    [Fact]
    public void Criar_CalculaVariosItensEmCentavosEPreservaSnapshots()
    {
        var primeiro = CriarItem() with { NomeProduto = " Sabonete ", NomeVariante = " Lavanda " };
        var dados = new List<DadosItemPedido>
        {
            primeiro, CriarItem() with { Quantidade = 3, PrecoUnitarioCentavos = 105 },
            CriarItem() with { PrecoUnitarioCentavos = 0 }
        };
        var pedido = Pedido.Criar(Cliente, Agora, dados);
        dados[0] = primeiro with { NomeProduto = "Outro", NomeVariante = "Outra", PrecoUnitarioCentavos = 900 };
        dados.Clear();

        Assert.Equal([398L, 315L, 0L], pedido.Itens.Select(item => item.SubtotalCentavos));
        Assert.Equal(713, pedido.TotalCentavos);
        Assert.Equal("Sabonete", pedido.Itens[0].NomeProduto);
        Assert.Equal("Lavanda", pedido.Itens[0].NomeVariante);
        Assert.Equal(199, pedido.Itens[0].PrecoUnitarioCentavos);
    }

    [Fact]
    public void Criar_RejeitaItensVaziosDuplicadosQuantidadesPrecosIdentificadoresENomesInvalidos()
    {
        var item = CriarItem();
        Assert.Throws<ArgumentException>("itens", () => Pedido.Criar(Cliente, Agora, []));
        Assert.Throws<ArgumentException>("itens", () => Pedido.Criar(Cliente, Agora, [item, item]));
        Assert.Throws<ArgumentOutOfRangeException>("clienteId", () => Pedido.Criar(Guid.Empty, Agora, [item]));
        Assert.Throws<ArgumentOutOfRangeException>("ProdutoId", () =>
            Pedido.Criar(Cliente, Agora, [item with { ProdutoId = Guid.Empty }]));
        Assert.Throws<ArgumentOutOfRangeException>("ProdutoVarianteId", () =>
            Pedido.Criar(Cliente, Agora, [item with { ProdutoVarianteId = Guid.Empty }]));
        Assert.Throws<ArgumentOutOfRangeException>("Quantidade", () =>
            Pedido.Criar(Cliente, Agora, [item with { Quantidade = 0 }]));
        Assert.Throws<ArgumentOutOfRangeException>("Quantidade", () =>
            Pedido.Criar(Cliente, Agora, [item with { Quantidade = -1 }]));
        Assert.Throws<ArgumentOutOfRangeException>("PrecoUnitarioCentavos", () =>
            Pedido.Criar(Cliente, Agora, [item with { PrecoUnitarioCentavos = -1 }]));
        Assert.Throws<ArgumentException>("NomeProduto", () =>
            Pedido.Criar(Cliente, Agora, [item with { NomeProduto = "  " }]));
        Assert.Throws<ArgumentException>("NomeVariante", () =>
            Pedido.Criar(Cliente, Agora, [item with { NomeVariante = new string('A', ProdutoVariante.IdentificacaoMaxLength + 1) }]));
    }

    [Fact]
    public void Criar_RejeitaOverflowEmLinhaOuTotalEUtcInvalido()
    {
        var item = CriarItem() with { PrecoUnitarioCentavos = long.MaxValue };
        Assert.Throws<ArgumentOutOfRangeException>("itens", () => Pedido.Criar(Cliente, Agora, [item]));
        Assert.Throws<ArgumentOutOfRangeException>("itens", () =>
            Pedido.Criar(Cliente, Agora, [item with { Quantidade = 1 }, CriarItem()]));
        Assert.Throws<ArgumentException>("criadoEmUtc", () =>
            Pedido.Criar(Cliente, Agora.ToOffset(TimeSpan.FromHours(-3)), [CriarItem()]));
        Assert.Throws<ArgumentOutOfRangeException>("criadoEmUtc", () =>
            Pedido.Criar(Cliente, DateTimeOffset.MaxValue, [CriarItem()]));
    }

    [Fact]
    public void Transicoes_ExigemSequenciaERegistramAuditoriaAteEntrega()
    {
        var pedido = Pedido.Criar(Cliente, Agora, [CriarItem()]);
        Assert.Throws<InvalidOperationException>(() => pedido.IniciarPreparacao(Responsavel, Agora));
        Assert.Throws<InvalidOperationException>(() => pedido.AutorizarRetirada(Responsavel, Agora));
        Assert.Throws<InvalidOperationException>(() => pedido.ConfirmarEntrega(Responsavel, Agora));
        Assert.Throws<ArgumentOutOfRangeException>(() => pedido.ConfirmarPagamento(Guid.Empty, Agora));

        var pagamento = pedido.PrazoPagamentoEmUtc.AddTicks(-1);
        pedido.ConfirmarPagamento(Responsavel, pagamento);
        Assert.Equal(SituacaoPedido.PagamentoConfirmado, pedido.Situacao);
        Assert.Throws<InvalidOperationException>(() => pedido.ConfirmarPagamento(Responsavel, pagamento));
        Assert.Throws<InvalidOperationException>(() => pedido.Expirar(pedido.PrazoPagamentoEmUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => pedido.IniciarPreparacao(Responsavel, pagamento.AddTicks(-1)));
        var preparacao = pagamento.AddDays(1);
        pedido.IniciarPreparacao(Responsavel, preparacao);
        Assert.Equal(SituacaoPedido.EmPreparacao, pedido.Situacao);
        Assert.Throws<InvalidOperationException>(() => pedido.ConfirmarEntrega(Responsavel, preparacao));
        var retirada = preparacao.AddHours(1);
        pedido.AutorizarRetirada(Responsavel, retirada);
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, pedido.Situacao);
        Assert.Null(pedido.EncerradoEmUtc);
        var entrega = retirada.AddHours(2);
        pedido.ConfirmarEntrega(Responsavel, entrega);

        Assert.Equal(SituacaoPedido.Entregue, pedido.Situacao);
        Assert.Equal(pagamento, pedido.PagamentoConfirmadoEmUtc);
        Assert.Equal(Responsavel, pedido.PagamentoConfirmadoPorId);
        Assert.Equal(preparacao, pedido.PreparacaoIniciadaEmUtc);
        Assert.Equal(Responsavel, pedido.PreparacaoIniciadaPorId);
        Assert.Equal(retirada, pedido.RetiradaAutorizadaEmUtc);
        Assert.Equal(Responsavel, pedido.RetiradaAutorizadaPorId);
        Assert.Equal(entrega, pedido.EncerradoEmUtc);
        Assert.Equal(Responsavel, pedido.EncerradoPorId);
        Assert.Throws<InvalidOperationException>(() => pedido.Cancelar(Responsavel, entrega, "Cancelamento"));
    }

    [Fact]
    public void Prazo_ExpiraNoLimiteEImpedeConfirmacaoTardia()
    {
        var pedido = Pedido.Criar(Cliente, Agora, [CriarItem()]);
        var prazo = pedido.PrazoPagamentoEmUtc;
        Assert.Throws<InvalidOperationException>(() => pedido.Expirar(prazo.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => pedido.ConfirmarPagamento(Responsavel, prazo));
        Assert.Throws<InvalidOperationException>(() => pedido.Cancelar(Responsavel, prazo, "Cancelamento"));
        pedido.Expirar(prazo);
        Assert.Equal(SituacaoPedido.Expirado, pedido.Situacao);
        Assert.Equal(prazo, pedido.EncerradoEmUtc);
        Assert.Null(pedido.EncerradoPorId);
        Assert.Throws<InvalidOperationException>(() => pedido.ConfirmarPagamento(Responsavel, prazo.AddTicks(1)));
    }

    [Fact]
    public void Cancelar_ExigeMotivoEPreservaRegistroDePagamentoConfirmado()
    {
        var pedido = Pedido.Criar(Cliente, Agora, [CriarItem()]);
        pedido.ConfirmarPagamento(Responsavel, Agora);
        Assert.Throws<ArgumentException>("motivo", () => pedido.Cancelar(Responsavel, Agora, " "));
        var encerramento = Agora.AddDays(2);
        pedido.Cancelar(Responsavel, encerramento, " Cancelamento autorizado ");
        Assert.Equal(SituacaoPedido.Cancelado, pedido.Situacao);
        Assert.Equal("Cancelamento autorizado", pedido.MotivoCancelamento);
        Assert.Equal(Agora, pedido.PagamentoConfirmadoEmUtc);
        Assert.Equal(encerramento, pedido.EncerradoEmUtc);
        Assert.Equal(Responsavel, pedido.EncerradoPorId);
        Assert.Throws<InvalidOperationException>(() => pedido.IniciarPreparacao(Responsavel, encerramento));
    }

    private static DadosItemPedido CriarItem() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Sabonete", "Lavanda", 2, 199);
}
