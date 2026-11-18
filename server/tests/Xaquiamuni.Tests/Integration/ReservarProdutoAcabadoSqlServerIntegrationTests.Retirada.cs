using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task Retirada_DeUmLoteGravaBaixaHistoricoEVinculoEReapresentaResultado()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var lote = (await EntrarAsync(context, variante.Id, 3)).Lote!;
        var pedido = Guid.NewGuid();
        var reserva = Assert.Single((await CasoDeUso(context).ExecuteAsync(new(pedido, variante.Id, 2))).Reservas);
        Assert.Equal(TransicaoReservasStatus.Sucesso, await Repositorio(context).ConfirmarCompromissoAsync(pedido));
        var retirada = database.Services.GetRequiredService<IConsumirReservasProdutoAcabado>();
        var primeira = await retirada.ConsumirAsync(pedido, Responsavel);
        var repetida = await retirada.ConsumirAsync(pedido, Guid.NewGuid());

        Assert.Equal(ConsumirReservasStatus.Sucesso, primeira.Status);
        Assert.Equal(ConsumirReservasStatus.Repetida, repetida.Status);
        var movimento = Assert.Single(primeira.Movimentos);
        Assert.Equal(movimento.Id, Assert.Single(repetida.Movimentos).Id);
        var original = await context.MovimentosProdutoAcabado.AsNoTracking().SingleAsync(m => m.Id == movimento.Id);
        Assert.Equal(reserva.Id, original.ReservaProdutoAcabadoId);
        Assert.Equal(lote.Id, original.LoteId);
        Assert.Equal(Responsavel, original.ResponsavelId);
        Assert.Equal("Retirada de pedido", original.Motivo);
        Assert.Equal(2, original.Quantidade);
        Assert.Equal(3, original.SaldoAnterior.QuantidadeVendavel);
        Assert.Equal(1, original.SaldoPosterior.QuantidadeVendavel);
        Assert.Equal(TimeSpan.Zero, original.RegistradoEmUtc.Offset);
        var consumida = await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(pedido, consumida.PedidoId);
        Assert.Equal(EstadoReservaProdutoAcabado.Consumida, consumida.Estado);
        Assert.Equal(original.RegistradoEmUtc, consumida.EncerradaEmUtc);
        Assert.Equal(OperacaoProdutoAcabadoStatus.MovimentoNaoEncontrado, (await Repositorio(context)
            .EstornarAsync(new(movimento.Id, Responsavel, "Estorno", "estorno-retirada"))).Status);
        Assert.Equal(1, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(2, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Retirada_VariosLotesEVariantesTemUmaUnicaBaixaEmDuasChamadasConcorrentes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var pedido = await PrepararRetiradaComTresLotesAsync(context);
        await using var conexaoA = database.CreateVerificationContext();
        await using var conexaoB = database.CreateVerificationContext();

        var (primeira, segunda) = await DisputarAsync(
            () => Repositorio(conexaoA).ConsumirAsync(pedido, Responsavel),
            () => Repositorio(conexaoB).ConsumirAsync(pedido, Guid.NewGuid()));

        var resultados = new[] { primeira, segunda };
        var sucesso = Assert.Single(resultados, r => r.Status == ConsumirReservasStatus.Sucesso);
        var repetida = Assert.Single(resultados, r => r.Status == ConsumirReservasStatus.Repetida);
        Assert.Equal(sucesso.Movimentos.Select(m => m.Id), repetida.Movimentos.Select(m => m.Id));
        Assert.Equal(3, sucesso.Movimentos.Count);
        Assert.Equal(3, sucesso.Movimentos.Select(m => m.ReservaProdutoAcabadoId).Distinct().Count());
        Assert.Single(sucesso.Movimentos.Select(m => m.RegistradoEmUtc).Distinct());
        Assert.All(await context.ReservasProdutoAcabado.ToListAsync(),
            r => Assert.Equal(EstadoReservaProdutoAcabado.Consumida, r.Estado));
        Assert.All(await context.SaldosProdutoAcabado.ToListAsync(), s => Assert.Equal(0, s.QuantidadeFisica));
        Assert.Equal(6, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Retirada_FalhaDepoisDaPrimeiraBaixaReverteTodoOPedido()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var pedido = await PrepararRetiradaComTresLotesAsync(context);
        var interceptor = new FalharDepoisDaPrimeiraBaixa();
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(context.Database.GetConnectionString(), sql => sql.MaxBatchSize(1))
            .AddInterceptors(interceptor).Options;
        await using var contextoComFalha = new XaquiamuniDbContext(options);

        await Assert.ThrowsAnyAsync<Exception>(() => Repositorio(contextoComFalha).ConsumirAsync(pedido, Responsavel));

        Assert.Equal(1, interceptor.BaixasExecutadas);
        Assert.All(await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync(), r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado);
            Assert.Null(r.EncerradaEmUtc);
        });
        Assert.All(await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync(), s => Assert.Equal(1, s.QuantidadeFisica));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(ConsumirReservasStatus.Sucesso, (await Repositorio(context).ConsumirAsync(pedido, Responsavel)).Status);
    }

    [SqlServerIntegrationFact]
    public async Task Retirada_BloqueiaPedidoSeQualquerLoteEstiverVencidoOuInsuficiente()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var valido = (await EntrarAsync(context, variante.Id, 1)).Lote!;
        var vencido = (await EntrarAsync(context, variante.Id, 1, validade: Hoje.AddDays(-1))).Lote!;
        var insuficiente = (await EntrarAsync(context, variante.Id, 1)).Lote!;
        var agora = await LerUtcSqlAsync(context);
        var pedidoVencido = Guid.NewGuid();
        var pedidoInsuficiente = Guid.NewGuid();
        context.ReservasProdutoAcabado.AddRange(
            CriarConfirmada(pedidoVencido, variante.Id, valido.Id, 1, agora),
            CriarConfirmada(pedidoVencido, variante.Id, vencido.Id, 1, agora),
            CriarConfirmada(pedidoInsuficiente, variante.Id, insuficiente.Id, 2, agora));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var retirada = Repositorio(context);
        Assert.Equal(ConsumirReservasStatus.LoteInelegivel, (await retirada.ConsumirAsync(pedidoVencido, Responsavel)).Status);
        Assert.Equal(ConsumirReservasStatus.SaldoInsuficiente, (await retirada.ConsumirAsync(pedidoInsuficiente, Responsavel)).Status);
        Assert.All(await context.ReservasProdutoAcabado.ToListAsync(),
            r => Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado));
        Assert.All(await context.SaldosProdutoAcabado.ToListAsync(), s => Assert.Equal(1, s.QuantidadeFisica));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Retirada_RejeitaAtivaExpiradaLiberadaEMisturaDeEstados()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var primeiro = (await EntrarAsync(context, variante.Id, 4)).Lote!;
        var segundo = (await EntrarAsync(context, variante.Id, 4)).Lote!;
        var agora = await LerUtcSqlAsync(context);
        var ativa = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, primeiro.Id, 1, agora);
        var expirada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, primeiro.Id, 1, agora.AddDays(-2));
        expirada.Expirar(agora);
        var liberada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, primeiro.Id, 1, agora);
        liberada.Liberar(agora, "Cancelamento autorizado");
        var mista = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, primeiro.Id, 1, agora);
        context.ReservasProdutoAcabado.AddRange(ativa, expirada, liberada, mista,
            CriarConfirmada(mista.PedidoId, variante.Id, segundo.Id, 1, agora));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        foreach (var reserva in new[] { ativa, expirada, liberada, mista })
        {
            Assert.Equal(ConsumirReservasStatus.EstadoIncompativel,
                (await Repositorio(context).ConsumirAsync(reserva.PedidoId, Responsavel)).Status);
        }

        Assert.Equal(2, await context.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(8, (await context.SaldosProdutoAcabado.ToListAsync()).Sum(s => s.QuantidadeFisica));
    }

    [SqlServerIntegrationFact]
    public async Task Retirada_ConcorreComSaidaPerdaELiberacaoSemConsumirDuasVezes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        await using var conexaoA = database.CreateVerificationContext();
        await using var conexaoB = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var retirada = Repositorio(conexaoA);
        var concorrente = Repositorio(conexaoB);
        foreach (var tipo in new[] { TipoMovimentoProdutoAcabado.SaidaManual, TipoMovimentoProdutoAcabado.Perda })
        {
            var lote = (await EntrarAsync(context, variante.Id, 2)).Lote!;
            var pedido = Guid.NewGuid();
            Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, variante.Id, 2))).Sucesso);
            Assert.Equal(TransicaoReservasStatus.Sucesso, await Repositorio(context).ConfirmarCompromissoAsync(pedido));
            var (resultado, operacao) = await DisputarAsync(
                () => retirada.ConsumirAsync(pedido, Responsavel),
                () => concorrente.RegistrarOperacaoAsync(Operacao(variante.Id, lote.Id, tipo, 1)));
            Assert.Equal(ConsumirReservasStatus.Sucesso, resultado.Status);
            Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, operacao.Status);
            var saldo = await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync(s => s.LoteId == lote.Id);
            Assert.Equal(0, saldo.QuantidadeFisica);
        }

        var ultimoLote = (await EntrarAsync(context, variante.Id, 1)).Lote!;
        var pedidoFinal = Guid.NewGuid();
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedidoFinal, variante.Id, 1))).Sucesso);
        Assert.Equal(TransicaoReservasStatus.Sucesso, await Repositorio(context).ConfirmarCompromissoAsync(pedidoFinal));
        var (consumo, liberacao) = await DisputarAsync(
            () => retirada.ConsumirAsync(pedidoFinal, Responsavel),
            () => concorrente.LiberarAsync(pedidoFinal, "Cancelamento autorizado"));
        var consumiu = consumo.Status == ConsumirReservasStatus.Sucesso;
        Assert.Equal(consumiu ? TransicaoReservasStatus.EstadoIncompativel : TransicaoReservasStatus.Sucesso, liberacao);
        Assert.Equal(consumiu ? ConsumirReservasStatus.Sucesso : ConsumirReservasStatus.EstadoIncompativel, consumo.Status);
        Assert.Equal(consumiu ? 2 : 1, await context.MovimentosProdutoAcabado.CountAsync(m => m.LoteId == ultimoLote.Id));
        var saldoFinal = await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync(s => s.LoteId == ultimoLote.Id);
        Assert.Equal(consumiu ? 0 : 1, saldoFinal.QuantidadeFisica);
    }

    private static async Task<Guid> PrepararRetiradaComTresLotesAsync(XaquiamuniDbContext context)
    {
        var variante = await CriarVarianteAsync(context);
        var outra = await CriarVarianteAsync(context, "Velas");
        await EntrarAsync(context, variante.Id, 1, validade: Hoje);
        await EntrarAsync(context, variante.Id, 1);
        await EntrarAsync(context, outra.Id, 1);
        var pedido = Guid.NewGuid();
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, variante.Id, 2))).Sucesso);
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, outra.Id, 1))).Sucesso);
        Assert.Equal(TransicaoReservasStatus.Sucesso, await Repositorio(context).ConfirmarCompromissoAsync(pedido));
        return pedido;
    }

    private static ReservaProdutoAcabado CriarConfirmada(
        Guid pedido, Guid variante, Guid lote, int quantidade, DateTimeOffset agora)
    {
        var reserva = ReservaProdutoAcabado.Criar(pedido, variante, lote, quantidade, agora);
        reserva.ConfirmarCompromisso(agora);
        return reserva;
    }

    private sealed class FalharDepoisDaPrimeiraBaixa : DbCommandInterceptor
    {
        public int BaixasExecutadas { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [SaldosProdutoAcabado]", StringComparison.Ordinal))
            {
                BaixasExecutadas++;
                await result.DisposeAsync();
                throw new InvalidOperationException("Falha de teste após a primeira baixa física.");
            }

            return result;
        }
    }
}
