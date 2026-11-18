using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class CicloReservasProdutoAcabadoTests
{
    private static readonly DateTimeOffset CriadaEm = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid PedidoId = Guid.NewGuid();

    [Fact]
    public void ConfirmaTodasAteOTickAnteriorAoPrazoEReaplicaSemAlterarAConfirmacao()
    {
        var reservas = new[] { Criar(CriadaEm), Criar(CriadaEm) };
        var instante = reservas[0].ExpiraEmUtc.AddTicks(-1);

        Assert.Equal(TransicaoReservasStatus.Sucesso,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Confirmar, instante));
        Assert.Equal(TransicaoReservasStatus.Repetida,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Confirmar, instante.AddDays(1)));
        Assert.All(reservas, reserva =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado);
            Assert.Equal(instante, reserva.ConfirmadaEmUtc);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConfirmacaoNoLimiteOuDepoisRejeitaTodoOPedido(int ticks)
    {
        var inicial = Criar(CriadaEm);
        var reservas = new[] { Criar(CriadaEm.AddHours(1), inicial.ExpiraEmUtc), inicial };
        var instante = reservas[1].ExpiraEmUtc.AddTicks(ticks);

        Assert.Equal(TransicaoReservasStatus.PrazoEncerrado,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Confirmar, instante));
        Assert.All(reservas, reserva => Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado));
    }

    [Theory]
    [InlineData(EstadoReservaProdutoAcabado.Confirmada)]
    [InlineData(EstadoReservaProdutoAcabado.Liberada)]
    [InlineData(EstadoReservaProdutoAcabado.Expirada)]
    public void ConfirmacaoRejeitaEstadoMistoSemModificarAsAlocacoes(EstadoReservaProdutoAcabado estado)
    {
        var outra = Criar(CriadaEm);
        switch (estado)
        {
            case EstadoReservaProdutoAcabado.Confirmada: outra.ConfirmarCompromisso(CriadaEm); break;
            case EstadoReservaProdutoAcabado.Liberada: outra.Liberar(CriadaEm, "Cancelamento"); break;
            case EstadoReservaProdutoAcabado.Expirada: outra.Expirar(outra.ExpiraEmUtc); break;
        }
        var ativa = Criar(CriadaEm.AddMinutes(1), outra.ExpiraEmUtc);

        Assert.Equal(TransicaoReservasStatus.EstadoIncompativel,
            CicloReservasProdutoAcabado.Aplicar([ativa, outra], TransicaoReservas.Confirmar, outra.ExpiraEmUtc));
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, ativa.Estado);
        Assert.Equal(estado, outra.Estado);
    }

    [Fact]
    public void ExpiracaoEncerraTodasAsAlocacoesNoPrazoComum()
    {
        var inicial = Criar(CriadaEm);
        var prazo = inicial.ExpiraEmUtc;
        var reservas = new[] { inicial, Criar(CriadaEm.AddMinutes(1), prazo) };

        Assert.Equal(TransicaoReservasStatus.PrazoNaoEncerrado,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Expirar, prazo.AddTicks(-1)));
        Assert.All(reservas, reserva => Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado));
        Assert.Equal(TransicaoReservasStatus.Sucesso,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Expirar, prazo));
        Assert.Equal(TransicaoReservasStatus.Repetida,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Expirar, prazo.AddMinutes(1)));
        Assert.All(reservas, reserva => Assert.Equal(prazo, reserva.EncerradaEmUtc));
    }

    [Fact]
    public void ConjuntoComPrazosDiferentesExigeReconciliacao()
    {
        var reservas = new[] { Criar(CriadaEm), Criar(CriadaEm.AddMinutes(1)) };
        Assert.Equal(TransicaoReservasStatus.EstadoIncompativel,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Expirar, reservas[1].ExpiraEmUtc));
        Assert.All(reservas, reserva => Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado));
    }

    [Fact]
    public void LiberacaoExigeMotivoERepeticaoPreservaOOriginal()
    {
        var ativa = Criar(CriadaEm);
        var confirmada = Criar(CriadaEm);
        confirmada.ConfirmarCompromisso(CriadaEm);
        var reservas = new[] { ativa, confirmada };
        var instante = CriadaEm.AddMinutes(1);

        Assert.Equal(TransicaoReservasStatus.SolicitacaoInvalida,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Liberar, instante, " "));
        Assert.Equal(TransicaoReservasStatus.Sucesso,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Liberar, instante, " Cancelamento "));
        Assert.Equal(TransicaoReservasStatus.Repetida,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Liberar, instante.AddDays(1), "Cancelamento"));
        Assert.Equal(TransicaoReservasStatus.ConflitoIdempotencia,
            CicloReservasProdutoAcabado.Aplicar(reservas, TransicaoReservas.Liberar, instante.AddDays(1), "Outro motivo"));
        Assert.All(reservas, reserva =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Liberada, reserva.Estado);
            Assert.Equal("Cancelamento", reserva.MotivoEncerramento);
            Assert.Equal(instante, reserva.EncerradaEmUtc);
        });
    }

    private static ReservaProdutoAcabado Criar(DateTimeOffset criadaEm, DateTimeOffset? expiraEm = null) =>
        ReservaProdutoAcabado.Criar(PedidoId, Guid.NewGuid(), Guid.NewGuid(), 2, criadaEm, expiraEm);
}
