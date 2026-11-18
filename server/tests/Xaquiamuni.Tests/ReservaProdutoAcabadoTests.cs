using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class ReservaProdutoAcabadoTests
{
    private static readonly DateTimeOffset CriadaEm = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DiaOperacional = new(2026, 11, 18);
    private static readonly Guid PedidoId = Guid.NewGuid();

    [Fact]
    public void Criar_GeraAlocacoesIndependentesPorLoteDoMesmoPedidoEVariante()
    {
        var (variante, primeiroLote, saldo) = CriarEstoque();
        var segundoLote = LoteProdutoAcabado.Criar(variante, null, DiaOperacional, DiaOperacional.AddDays(2));

        var primeira = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, primeiroLote.Id, 2, CriadaEm);
        var segunda = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, segundoLote.Id, 1, CriadaEm);

        Assert.NotEqual(Guid.Empty, primeira.Id);
        Assert.NotEqual(primeira.Id, segunda.Id);
        Assert.Equal(PedidoId, primeira.PedidoId);
        Assert.Equal(primeira.PedidoId, segunda.PedidoId);
        Assert.Equal(variante.Id, primeira.ProdutoVarianteId);
        Assert.Equal(variante.Id, segunda.ProdutoVarianteId);
        Assert.Equal(primeiroLote.Id, primeira.LoteId);
        Assert.Equal(segundoLote.Id, segunda.LoteId);
        Assert.Equal(2, primeira.Quantidade);
        Assert.Equal(1, segunda.Quantidade);
        Assert.Equal(CriadaEm, primeira.CriadaEmUtc);
        Assert.Equal(CriadaEm.AddHours(24), primeira.ExpiraEmUtc);
        Assert.Equal(TimeSpan.Zero, primeira.ExpiraEmUtc.Offset);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, primeira.Estado);
        Assert.Null(primeira.ConfirmadaEmUtc);
        Assert.Null(primeira.EncerradaEmUtc);
        Assert.Null(primeira.MotivoEncerramento);
        Assert.Equal(3, saldo.QuantidadeFisica);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_RejeitaQuantidadeNaoPositiva(int quantidade)
    {
        Assert.Throws<ArgumentOutOfRangeException>("quantidade", () =>
            ReservaProdutoAcabado.Criar(PedidoId, Guid.NewGuid(), Guid.NewGuid(), quantidade, CriadaEm));
    }

    [Theory]
    [InlineData("pedidoId")]
    [InlineData("produtoVarianteId")]
    [InlineData("loteId")]
    public void Criar_RejeitaIdentificadorVazio(string parametro)
    {
        Assert.Throws<ArgumentOutOfRangeException>(parametro, () => ReservaProdutoAcabado.Criar(
            parametro == "pedidoId" ? Guid.Empty : PedidoId,
            parametro == "produtoVarianteId" ? Guid.Empty : Guid.NewGuid(),
            parametro == "loteId" ? Guid.Empty : Guid.NewGuid(),
            1,
            CriadaEm));
    }

    [Fact]
    public void Validade_UsaUtcEIncluiCriacaoMasExcluiInstanteDaExpiracao()
    {
        var reserva = CriarReserva();
        Assert.False(reserva.EstaAtivaEm(CriadaEm.AddTicks(-1)));
        Assert.True(reserva.EstaAtivaEm(CriadaEm));
        Assert.True(reserva.EstaAtivaEm(reserva.ExpiraEmUtc.AddTicks(-1)));
        Assert.False(reserva.EstaAtivaEm(reserva.ExpiraEmUtc));
        Assert.False(reserva.EstaAtivaEm(reserva.ExpiraEmUtc.AddTicks(1)));
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado);

        var comOffset = CriadaEm.ToOffset(TimeSpan.FromHours(-3));
        Assert.Throws<ArgumentException>("criadaEmUtc", () =>
            ReservaProdutoAcabado.Criar(PedidoId, Guid.NewGuid(), Guid.NewGuid(), 1, comOffset));
        Assert.Throws<ArgumentException>("instanteUtc", () => reserva.EstaAtivaEm(comOffset));
        Assert.Throws<ArgumentException>("confirmadaEmUtc", () => reserva.ConfirmarCompromisso(comOffset));
        Assert.Throws<ArgumentOutOfRangeException>("confirmadaEmUtc", () =>
            reserva.ConfirmarCompromisso(CriadaEm.AddTicks(-1)));
        Assert.Throws<ArgumentException>("encerradaEmUtc", () => reserva.Liberar(comOffset, "Cancelado"));
        Assert.Throws<ArgumentOutOfRangeException>("encerradaEmUtc", () =>
            reserva.Liberar(CriadaEm.AddTicks(-1), "Cancelado"));
        Assert.Throws<ArgumentOutOfRangeException>("criadaEmUtc", () =>
            ReservaProdutoAcabado.Criar(PedidoId, Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Liberar_RegistraMotivoEInstanteUmaUnicaVez()
    {
        var reserva = CriarReserva();
        var encerradaEm = CriadaEm.AddHours(2);
        Assert.Throws<ArgumentException>("motivo", () => reserva.Liberar(encerradaEm, " "));
        Assert.True(reserva.Liberar(encerradaEm, " Cancelamento registrado "));
        Assert.False(reserva.Liberar(encerradaEm.AddHours(1), "Outro motivo"));
        Assert.Equal(EstadoReservaProdutoAcabado.Liberada, reserva.Estado);
        Assert.Equal(encerradaEm, reserva.EncerradaEmUtc);
        Assert.Equal("Cancelamento registrado", reserva.MotivoEncerramento);
        Assert.False(reserva.EstaAtivaEm(encerradaEm));
        Assert.Throws<InvalidOperationException>(() => reserva.Expirar(reserva.ExpiraEmUtc));
    }

    [Fact]
    public void Expirar_EncerraNoLimiteSemAlterarOPrimeiroEncerramento()
    {
        var reserva = CriarReserva();
        Assert.Throws<InvalidOperationException>(() => reserva.Expirar(reserva.ExpiraEmUtc.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => reserva.Liberar(reserva.ExpiraEmUtc, "Cancelado"));
        Assert.True(reserva.Expirar(reserva.ExpiraEmUtc));
        Assert.False(reserva.Expirar(reserva.ExpiraEmUtc.AddHours(2)));
        Assert.Equal(EstadoReservaProdutoAcabado.Expirada, reserva.Estado);
        Assert.Equal(reserva.ExpiraEmUtc, reserva.EncerradaEmUtc);
    }

    [Fact]
    public void ConfirmarCompromisso_MantemReservaAtivaAposPrazoESemBaixaFisica()
    {
        var (variante, lote, saldo) = CriarEstoque();
        var reserva = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, lote.Id, 2, CriadaEm);
        var confirmadaEm = reserva.ExpiraEmUtc.AddTicks(-1);

        Assert.True(reserva.ConfirmarCompromisso(confirmadaEm));
        Assert.False(reserva.ConfirmarCompromisso(reserva.ExpiraEmUtc.AddDays(1)));
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado);
        Assert.Equal(confirmadaEm, reserva.ConfirmadaEmUtc);
        Assert.Null(reserva.EncerradaEmUtc);
        Assert.True(reserva.EstaAtivaEm(reserva.ExpiraEmUtc));
        Assert.True(reserva.EstaAtivaEm(reserva.ExpiraEmUtc.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => reserva.Expirar(reserva.ExpiraEmUtc));
        Assert.Equal(3, saldo.QuantidadeVendavel);
        Assert.Equal(3, saldo.QuantidadeFisica);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConfirmarCompromisso_RejeitaNoPrazoLimiteOuDepois(int ticksAposExpiracao)
    {
        var reserva = CriarReserva();

        Assert.Throws<InvalidOperationException>(() =>
            reserva.ConfirmarCompromisso(reserva.ExpiraEmUtc.AddTicks(ticksAposExpiracao)));
        Assert.Null(reserva.ConfirmadaEmUtc);
        Assert.True(reserva.Expirar(reserva.ExpiraEmUtc));
        Assert.Throws<InvalidOperationException>(() =>
            reserva.ConfirmarCompromisso(reserva.ExpiraEmUtc.AddTicks(-1)));
        Assert.Equal(EstadoReservaProdutoAcabado.Expirada, reserva.Estado);
    }

    [Fact]
    public void Liberar_EncerraReservaConfirmadaAposPrazoComMotivo()
    {
        var reserva = CriarReserva();
        var confirmadaEm = CriadaEm.AddHours(1);
        Assert.True(reserva.ConfirmarCompromisso(confirmadaEm));
        Assert.Throws<ArgumentOutOfRangeException>("encerradaEmUtc", () =>
            reserva.Liberar(confirmadaEm.AddTicks(-1), "Cancelado"));

        var encerradaEm = reserva.ExpiraEmUtc.AddDays(2);
        Assert.Throws<ArgumentException>("motivo", () => reserva.Liberar(encerradaEm, " "));
        Assert.True(reserva.Liberar(encerradaEm, "Cancelamento autorizado"));
        Assert.False(reserva.Liberar(encerradaEm.AddHours(1), "Outro motivo"));
        Assert.Throws<InvalidOperationException>(() => reserva.ConfirmarCompromisso(confirmadaEm));
        Assert.Equal(EstadoReservaProdutoAcabado.Liberada, reserva.Estado);
        Assert.Equal(confirmadaEm, reserva.ConfirmadaEmUtc);
        Assert.Equal(encerradaEm, reserva.EncerradaEmUtc);
        Assert.Equal("Cancelamento autorizado", reserva.MotivoEncerramento);
        Assert.False(reserva.EstaAtivaEm(encerradaEm));
    }

    [Fact]
    public void Consumir_ExigeConfirmacaoEEncerraUmaVezAposPrazoSemBaixaFisica()
    {
        var (variante, lote, saldo) = CriarEstoque(diasDeValidade: 3);
        var reserva = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, lote.Id, 2, CriadaEm);
        var confirmadaEm = CriadaEm.AddHours(1);
        Assert.Throws<InvalidOperationException>(() =>
            reserva.Consumir(variante, lote, saldo, DiaOperacional, CriadaEm));
        Assert.True(reserva.ConfirmarCompromisso(confirmadaEm));
        Assert.Throws<ArgumentOutOfRangeException>("encerradaEmUtc", () =>
            reserva.Consumir(variante, lote, saldo, DiaOperacional, confirmadaEm.AddTicks(-1)));

        var encerradaEm = CriadaEm.AddDays(3);
        Assert.True(reserva.Consumir(variante, lote, saldo, lote.DataValidade, encerradaEm));
        Assert.False(reserva.Consumir(variante, lote, saldo, lote.DataValidade, encerradaEm));
        Assert.Throws<InvalidOperationException>(() => reserva.ConfirmarCompromisso(confirmadaEm));
        Assert.Equal(EstadoReservaProdutoAcabado.Consumida, reserva.Estado);
        Assert.Equal(confirmadaEm, reserva.ConfirmadaEmUtc);
        Assert.Equal(encerradaEm, reserva.EncerradaEmUtc);
        Assert.False(reserva.EstaAtivaEm(encerradaEm));
        Assert.Equal(2, reserva.Quantidade);
        Assert.Equal(variante.Id, reserva.ProdutoVarianteId);
        Assert.Equal(lote.Id, reserva.LoteId);
        Assert.Equal(3, saldo.QuantidadeVendavel);
        Assert.Equal(3, saldo.QuantidadeFisica);
        Assert.Throws<InvalidOperationException>(() => reserva.Liberar(encerradaEm, "Cancelado"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Consumir_RejeitaPrazoVencidoMesmoAntesDeRegistrarExpiracao(int ticksAposExpiracao)
    {
        var (variante, lote, saldo) = CriarEstoque();
        var reserva = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, lote.Id, 2, CriadaEm);
        var instante = reserva.ExpiraEmUtc.AddTicks(ticksAposExpiracao);

        Assert.Throws<InvalidOperationException>(() =>
            reserva.Consumir(variante, lote, saldo, DiaOperacional, instante));
        Assert.Null(reserva.EncerradaEmUtc);
        Assert.True(reserva.Expirar(instante));
        Assert.Throws<InvalidOperationException>(() =>
            reserva.Consumir(variante, lote, saldo, DiaOperacional, instante));
    }

    [Fact]
    public void Consumir_RejeitaReservaLiberadaELoteDiferente()
    {
        var (variante, lote, saldo) = CriarEstoque();
        var reserva = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, lote.Id, 2, CriadaEm);
        var outroLote = LoteProdutoAcabado.Criar(variante, null, DiaOperacional, lote.DataValidade);
        Assert.True(reserva.ConfirmarCompromisso(CriadaEm));
        Assert.Throws<ArgumentException>(() =>
            reserva.Consumir(variante, lote, SaldoLoteProdutoAcabado.CriarZero(outroLote), DiaOperacional, CriadaEm));
        Assert.True(reserva.Liberar(CriadaEm, "Cancelado"));
        Assert.Throws<InvalidOperationException>(() =>
            reserva.Consumir(variante, lote, saldo, DiaOperacional, CriadaEm));
    }

    [Theory]
    [InlineData(true, true, 3)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 1)]
    public void Consumir_RejeitaLoteInelegivelSemEncerrarReserva(
        bool varianteAtiva,
        bool loteVencido,
        int quantidadeVendavel)
    {
        var (variante, lote, saldo) = CriarEstoque(varianteAtiva, quantidadeVendavel, diasDeValidade: 2);
        var reserva = ReservaProdutoAcabado.Criar(PedidoId, variante.Id, lote.Id, 2, CriadaEm);
        Assert.True(reserva.ConfirmarCompromisso(CriadaEm.AddHours(1)));
        var diasAteRetirada = loteVencido ? 3 : 2;
        var dataReferencia = DiaOperacional.AddDays(diasAteRetirada);
        var retiradaEm = CriadaEm.AddDays(diasAteRetirada);

        Assert.Throws<InvalidOperationException>(() =>
            reserva.Consumir(variante, lote, saldo, dataReferencia, retiradaEm));
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado);
        Assert.True(reserva.EstaAtivaEm(retiradaEm));
        Assert.Null(reserva.EncerradaEmUtc);
        Assert.Equal(3, saldo.QuantidadeFisica);
    }

    private static ReservaProdutoAcabado CriarReserva() =>
        ReservaProdutoAcabado.Criar(PedidoId, Guid.NewGuid(), Guid.NewGuid(), 2, CriadaEm);

    [Fact]
    public void Criar_PrazoCompartilhadoPreservaInstanteDeCriacaoERejeitaLimitesInvalidos()
    {
        var variante = Guid.NewGuid();
        var lote = Guid.NewGuid();
        var prazo = CriadaEm.AddHours(24);
        var inclusao = CriadaEm.AddHours(4);

        var reserva = ReservaProdutoAcabado.Criar(PedidoId, variante, lote, 1, inclusao, prazo);
        Assert.Equal(inclusao, reserva.CriadaEmUtc);
        Assert.Equal(prazo, reserva.ExpiraEmUtc);
        Assert.Throws<ArgumentOutOfRangeException>("expiraEmUtc", () =>
            ReservaProdutoAcabado.Criar(PedidoId, variante, lote, 1, prazo, prazo));
        Assert.Throws<ArgumentOutOfRangeException>("expiraEmUtc", () =>
            ReservaProdutoAcabado.Criar(PedidoId, variante, lote, 1, CriadaEm, prazo.AddTicks(1)));
        Assert.Throws<ArgumentException>("expiraEmUtc", () =>
            ReservaProdutoAcabado.Criar(PedidoId, variante, lote, 1, CriadaEm, prazo.ToOffset(TimeSpan.FromHours(-3))));
    }

    private static (ProdutoVariante Variante, LoteProdutoAcabado Lote, SaldoLoteProdutoAcabado Saldo)
        CriarEstoque(bool varianteAtiva = true, int quantidadeVendavel = 3, int diasDeValidade = 0)
    {
        var produto = Produto.Criar(
            "Sabonete",
            null,
            Categoria.Criar("Sabonetes", null, 0),
            [
                ProdutoVariante.Criar("Padrão", "UN", 20m, padrao: true),
                ProdutoVariante.Criar("Lavanda", "UN", 15m)
            ]);
        var variante = produto.Variantes.Single(item => !item.Padrao);
        var lote = LoteProdutoAcabado.Criar(
            variante, null, DiaOperacional.AddDays(-1), DiaOperacional.AddDays(diasDeValidade));
        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        var responsavelId = Guid.NewGuid();
        saldo.RegistrarEntrada(
            lote, 3, CondicaoEstoqueProdutoAcabado.Vendavel, TipoMovimentoProdutoAcabado.EntradaManual,
            responsavelId, "Entrada de teste", CriadaEm.AddHours(-1), "entrada-teste", new byte[32]);
        if (quantidadeVendavel < 3)
        {
            saldo.TransferirCondicao(
                lote, 3 - quantidadeVendavel, CondicaoEstoqueProdutoAcabado.Vendavel,
                CondicaoEstoqueProdutoAcabado.Quarentena, responsavelId, "Separado para análise",
                CriadaEm, "quarentena-teste", new byte[32]);
        }
        if (!varianteAtiva)
        {
            produto.DesativarVariante(variante.Id);
        }
        return (variante, lote, saldo);
    }
}
