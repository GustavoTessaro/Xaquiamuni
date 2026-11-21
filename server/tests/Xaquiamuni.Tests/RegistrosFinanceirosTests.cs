using Xaquiamuni.Dominio.Financeiro;

namespace Xaquiamuni.Tests;

public sealed class RegistrosFinanceirosTests
{
    private static readonly DateOnly Data = new(2026, 1, 1);
    private static readonly DateTimeOffset Agora = new(2026, 1, 1, 10, 30, 0, TimeSpan.FromHours(-3));
    private static readonly Guid Autor = Guid.NewGuid();

    [Theory]
    [InlineData(TipoLancamento.Entrada)]
    [InlineData(TipoLancamento.Saida)]
    public void LancamentoMantemCentavosERegistraCancelamentoUmaVez(TipoLancamento tipo)
    {
        var lancamento = LancamentoFinanceiro.Criar(tipo, " Outros ", " Teste ", long.MaxValue, Data, MeioFinanceiro.Pix, Autor, Agora);
        Assert.Equal(tipo, lancamento.Tipo);
        Assert.Equal("Outros", lancamento.Categoria);
        Assert.Equal(long.MaxValue, lancamento.ValorCentavos);
        Assert.Equal(TimeSpan.Zero, lancamento.CriadoEmUtc.Offset);
        Assert.Throws<ArgumentException>(() => lancamento.Cancelar(" ", Autor, Agora));
        lancamento.Cancelar(" Ajuste ", Autor, Agora.AddDays(1));
        Assert.Equal("Ajuste", lancamento.MotivoCancelamento);
        Assert.Equal(Autor, lancamento.CanceladoPorId);
        Assert.Throws<ConflitoFinanceiroException>(() => lancamento.Cancelar("Outro", Guid.NewGuid(), Agora.AddDays(2)));
        Assert.Equal(Agora.AddDays(1), lancamento.CanceladoEmUtc);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void LancamentoRecusaValorNaoPositivo(long valor) =>
        Assert.Throws<ArgumentException>(() =>
            LancamentoFinanceiro.Criar(TipoLancamento.Entrada, "Outros", "Teste", valor, Data, MeioFinanceiro.Pix, Autor, Agora));

    [Fact]
    public void PagamentoExigeSaldoConhecidoESuficiente()
    {
        var compra = Guid.NewGuid();
        Assert.Throws<ConflitoFinanceiroException>(() =>
            PagamentoFornecedor.Registrar(compra, 1, null, Data, MeioFinanceiro.Pix, null, Autor, Agora));
        Assert.Throws<ConflitoFinanceiroException>(() =>
            PagamentoFornecedor.Registrar(compra, 2, 1, Data, MeioFinanceiro.Pix, null, Autor, Agora));
        Assert.Throws<ArgumentException>(() =>
            PagamentoFornecedor.Registrar(compra, 0, 1, Data, MeioFinanceiro.Pix, null, Autor, Agora));
        var pagamento = PagamentoFornecedor.Registrar(compra, 1, 1, Data, MeioFinanceiro.Pix, " ref ", Autor, Agora);
        Assert.Equal((compra, 1L, "ref"), (pagamento.CompraId, pagamento.ValorCentavos, pagamento.Referencia));
    }

    [Theory]
    [InlineData(TipoLancamento.Entrada)]
    [InlineData(TipoLancamento.Saida)]
    public void PendenciaMantemVencimentoERegistraQuitacaoIntegral(TipoLancamento tipo)
    {
        var registro = LancamentoFinanceiro.CriarPendente(tipo, "Teste", "Conta", 2000, Data,
            Autor, Agora, " Empresa ", " Nota ");
        Assert.Equal(SituacaoLancamento.Pendente, registro.Situacao);
        Assert.Equal("Empresa", registro.Contraparte);
        Assert.Equal("Nota", registro.Observacoes);
        Assert.Null(registro.Data);
        Assert.Null(registro.Meio);
        Assert.Throws<ArgumentException>(() => registro.Quitar(Data.AddDays(1), MeioFinanceiro.Pix, Autor, Agora, Data));

        var quitador = Guid.NewGuid();
        registro.Quitar(Data.AddDays(-1), MeioFinanceiro.Pix, quitador, Agora, Data);
        Assert.Equal(SituacaoLancamento.Realizado, registro.Situacao);
        Assert.Equal(Data, registro.Vencimento);
        Assert.Equal(Data.AddDays(-1), registro.Data);
        Assert.Equal(quitador, registro.QuitadoPorId);
        Assert.Equal(TimeSpan.Zero, registro.QuitadoEmUtc!.Value.Offset);
        Assert.Throws<ConflitoFinanceiroException>(() => registro.Quitar(Data, MeioFinanceiro.Pix, Autor, Agora, Data));
        Assert.Throws<ConflitoFinanceiroException>(() => registro.Cancelar("Ajuste", Autor, Agora));
    }

    [Fact]
    public void PendenciaExigeVencimentoERecusaQuitacaoDepoisDoCancelamento()
    {
        Assert.Throws<ArgumentException>(() => LancamentoFinanceiro.CriarPendente(
            TipoLancamento.Entrada, "Teste", "Conta", 100, null, Autor, Agora, null, null));
        var registro = LancamentoFinanceiro.CriarPendente(TipoLancamento.Entrada, "Teste", "Conta", 100, Data,
            Autor, Agora, null, null);
        registro.Cancelar("Conta cancelada", Autor, Agora);
        Assert.Equal(SituacaoLancamento.Cancelado, registro.Situacao);
        Assert.Null(registro.Data);
        Assert.Throws<ConflitoFinanceiroException>(() => registro.Quitar(Data, MeioFinanceiro.Pix, Autor, Agora, Data));
    }
}
