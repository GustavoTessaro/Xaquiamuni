using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task Disponibilidade_SomaPorLoteEVarianteSomenteCompromissosVigentes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var outra = await CriarVarianteAsync(context, "Velas");
        var valido = (await EntrarAsync(context, variante.Id, 8)).Lote!;
        var vencido = (await EntrarAsync(context, variante.Id, 4, validade: Hoje.AddDays(-1))).Lote!;
        var outroLote = (await EntrarAsync(context, outra.Id, 4)).Lote!;
        var semSaldo = LoteProdutoAcabado.Criar(variante, null, Hoje, Hoje);
        context.LotesProdutoAcabado.Add(semSaldo);
        await context.SaveChangesAsync();
        var consulta = new EfEstoqueProdutoAcabadoConsultaRepository(context);
        var semReservas = await consulta.ConsultarPosicaoVarianteAsync(variante.Id, Hoje);
        Assert.NotNull(semReservas);
        Assert.Equal(12, semReservas.Resumo.SaldoFisico);
        Assert.Equal(0, semReservas.Resumo.SaldoReservado);
        Assert.Equal(8, semReservas.Resumo.SaldoDisponivel);
        var loteSemSaldo = Assert.Single(semReservas.Lotes, l => l.LoteId == semSaldo.Id);
        Assert.False(loteSemSaldo.TemRegistroSaldo);
        Assert.Null(loteSemSaldo.Versao);

        var agora = await LerUtcSqlAsync(context);
        var ativa = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, valido.Id, 2, agora.AddHours(-1));
        var confirmada = CriarConfirmada(Guid.NewGuid(), variante.Id, valido.Id, 3, agora.AddDays(-2));
        var confirmadaVencida = CriarConfirmada(Guid.NewGuid(), variante.Id, vencido.Id, 2, agora.AddDays(-2));
        var ativaVencida = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, valido.Id, 1, agora.AddDays(-2));
        var expirada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, valido.Id, 1, agora.AddDays(-2));
        expirada.Expirar(agora);
        var liberada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, valido.Id, 1, agora.AddHours(-1));
        liberada.Liberar(agora, "Cancelamento autorizado");
        var estoqueOutra = ReservaProdutoAcabado.Criar(Guid.NewGuid(), outra.Id, outroLote.Id, 1, agora.AddHours(-1));
        context.ReservasProdutoAcabado.AddRange(
            ativa, confirmada, confirmadaVencida, ativaVencida, expirada, liberada, estoqueOutra);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var posicao = await consulta.ConsultarPosicaoVarianteAsync(variante.Id, Hoje);
        Assert.NotNull(posicao);
        Assert.Equal(12, posicao.Resumo.SaldoFisico);
        Assert.Equal(8, posicao.Resumo.SaldoUtilizavel);
        Assert.Equal(7, posicao.Resumo.SaldoReservado);
        Assert.Equal(3, posicao.Resumo.SaldoDisponivel);
        var posicaoValida = Assert.Single(posicao.Lotes, l => l.LoteId == valido.Id);
        Assert.Equal(5, posicaoValida.SaldoReservado);
        Assert.Equal(3, posicaoValida.SaldoDisponivel);
        Assert.NotNull(posicaoValida.Versao);
        var posicaoVencida = await consulta.ConsultarPosicaoLoteAsync(variante.Id, vencido.Id, Hoje);
        Assert.NotNull(posicaoVencida);
        Assert.False(posicaoVencida.Valido);
        Assert.Equal(4, posicaoVencida.QuantidadeVendavel);
        Assert.Equal(0, posicaoVencida.SaldoUtilizavel);
        Assert.Equal(2, posicaoVencida.SaldoReservado);
        Assert.Equal(0, posicaoVencida.SaldoDisponivel);
        Assert.Equal(3, (await consulta.ConsultarPosicaoVarianteAsync(outra.Id, Hoje))!.Resumo.SaldoDisponivel);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa,
            (await context.ReservasProdutoAcabado.SingleAsync(r => r.Id == ativaVencida.Id)).Estado);

        var pedidoNovo = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 4);
        Assert.Equal(ReservarProdutoAcabadoStatus.SaldoInsuficiente, (await CasoDeUso(context).ExecuteAsync(pedidoNovo)).Status);
        var alocacao = await CasoDeUso(context).ExecuteAsync(pedidoNovo with { Quantidade = posicao.Resumo.SaldoDisponivel });
        Assert.Equal(valido.Id, Assert.Single(alocacao.Reservas).LoteId);
        var apos = (await consulta.ConsultarPosicaoVarianteAsync(variante.Id, Hoje))!.Resumo;
        Assert.Equal(0, apos.SaldoDisponivel);
        Assert.Equal(SituacaoEstoqueProdutoAcabado.SemEstoque, apos.Situacao);
        Assert.Equal(12, apos.SaldoFisico);
    }

    [SqlServerIntegrationFact]
    public async Task Disponibilidade_ConsumoBaixaFisicoEDeixaDeComprometerSemDescontarDuasVezes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var lote = (await EntrarAsync(context, variante.Id, 5)).Lote!;
        var pedido = Guid.NewGuid();
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, variante.Id, 2))).Sucesso);
        var repositorio = Repositorio(context);
        Assert.Equal(TransicaoReservasStatus.Sucesso, await repositorio.ConfirmarCompromissoAsync(pedido));
        var consulta = new EfEstoqueProdutoAcabadoConsultaRepository(context);
        var antes = await consulta.ConsultarPosicaoLoteAsync(variante.Id, lote.Id, Hoje);
        Assert.NotNull(antes);
        Assert.Equal(5, antes.SaldoFisico);
        Assert.Equal(2, antes.SaldoReservado);
        Assert.Equal(3, antes.SaldoDisponivel);

        Assert.Equal(ConsumirReservasStatus.Sucesso, (await repositorio.ConsumirAsync(pedido, Responsavel)).Status);
        var depois = await consulta.ConsultarPosicaoLoteAsync(variante.Id, lote.Id, Hoje);
        Assert.NotNull(depois);
        Assert.Equal(3, depois.SaldoFisico);
        Assert.Equal(0, depois.SaldoReservado);
        Assert.Equal(3, depois.SaldoDisponivel);
        Assert.Equal(EstadoReservaProdutoAcabado.Consumida, (await context.ReservasProdutoAcabado.SingleAsync()).Estado);
    }
}
