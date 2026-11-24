using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
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
    public async Task Ciclo_ConfirmaVariosLotesEVariantesELiberaComMotivoSemBaixaFisica()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var pedido = Guid.NewGuid();
        var variante = await CriarVarianteAsync(context);
        var outra = await CriarVarianteAsync(context, "Velas");
        var terceiroLote = (await EntrarAsync(context, outra.Id, 1)).Lote!;
        await EntrarAsync(context, variante.Id, 1, validade: Hoje);
        await EntrarAsync(context, variante.Id, 1);
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, variante.Id, 2))).Sucesso);
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, outra.Id, 1))).Sucesso);
        var antes = await context.ReservasProdutoAcabado.AsNoTracking().ToDictionaryAsync(r => r.Id);
        var prazo = Assert.Single(antes.Values.Select(r => r.ExpiraEmUtc).Distinct());
        Assert.Equal(antes.Values.Min(r => r.CriadaEmUtc).AddHours(24), prazo);
        var ciclo = database.Services.GetRequiredService<ICicloReservasProdutoAcabado>();

        Assert.Equal(TransicaoReservasStatus.Sucesso, await ciclo.ConfirmarCompromissoAsync(pedido));
        Assert.Equal(TransicaoReservasStatus.Repetida, await ciclo.ConfirmarCompromissoAsync(pedido));
        Assert.Equal(TransicaoReservasStatus.EstadoIncompativel, await ciclo.ExpirarAsync(pedido));
        var confirmadas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(3, confirmadas.Count);
        var instante = Assert.Single(confirmadas.Select(r => r.ConfirmadaEmUtc).Distinct());
        Assert.NotNull(instante);
        Assert.Equal(TimeSpan.Zero, instante.Value.Offset);
        Assert.All(confirmadas, r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado);
            Assert.Equal(antes[r.Id].Quantidade, r.Quantidade);
            Assert.Equal(antes[r.Id].LoteId, r.LoteId);
            Assert.InRange(instante.Value, r.CriadaEmUtc, r.ExpiraEmUtc.AddTicks(-1));
        });
        var outroPedido = new ReservarProdutoAcabadoInput(Guid.NewGuid(), outra.Id, 1);
        Assert.Equal(ReservarProdutoAcabadoStatus.SaldoInsuficiente,
            (await CasoDeUso(context).ExecuteAsync(outroPedido)).Status);

        Assert.Equal(TransicaoReservasStatus.SolicitacaoInvalida, await ciclo.LiberarAsync(pedido, " "));
        Assert.Equal(TransicaoReservasStatus.Sucesso, await ciclo.LiberarAsync(pedido, " Cancelamento autorizado "));
        var liberadas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(TransicaoReservasStatus.Repetida, await ciclo.LiberarAsync(pedido, "Cancelamento autorizado"));
        Assert.Equal(TransicaoReservasStatus.ConflitoIdempotencia, await ciclo.LiberarAsync(pedido, "Outro motivo"));
        var encerramento = Assert.Single(liberadas.Select(r => r.EncerradaEmUtc).Distinct());
        Assert.NotNull(encerramento);
        Assert.All(await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync(), r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Liberada, r.Estado);
            Assert.Equal("Cancelamento autorizado", r.MotivoEncerramento);
            Assert.Equal(encerramento, r.EncerradaEmUtc);
            Assert.Equal(instante, r.ConfirmadaEmUtc);
        });
        var realocacao = await CasoDeUso(context).ExecuteAsync(outroPedido);
        Assert.Equal(terceiroLote.Id, Assert.Single(realocacao.Reservas).LoteId);
        Assert.Equal(3, (await context.SaldosProdutoAcabado.ToListAsync()).Sum(s => s.QuantidadeFisica));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Ciclo_RecusaMudancaParcialEExpiracaoRespeitaDisponibilidade()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var outro = await CriarVarianteAsync(context, "Velas");
        var vencido = (await EntrarAsync(context, variante.Id, 2)).Lote!;
        var vigente = (await EntrarAsync(context, outro.Id, 1)).Lote!;
        var agora = await LerUtcSqlAsync(context);
        var pedido = Guid.NewGuid();
        var isolada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, vencido.Id, 1, agora.AddDays(-2));
        context.ReservasProdutoAcabado.AddRange(isolada,
            ReservaProdutoAcabado.Criar(pedido, variante.Id, vencido.Id, 1, agora.AddDays(-2)),
            ReservaProdutoAcabado.Criar(pedido, outro.Id, vigente.Id, 1, agora.AddHours(-1)));
        await context.SaveChangesAsync();
        var ciclo = database.Services.GetRequiredService<ICicloReservasProdutoAcabado>();

        Assert.Equal(TransicaoReservasStatus.EstadoIncompativel, await ciclo.ConfirmarCompromissoAsync(pedido));
        Assert.Equal(TransicaoReservasStatus.EstadoIncompativel, await ciclo.LiberarAsync(pedido, "Cancelamento"));
        Assert.Equal(TransicaoReservasStatus.EstadoIncompativel, await ciclo.ExpirarAsync(pedido));
        Assert.All(await context.ReservasProdutoAcabado.AsNoTracking().Where(r => r.PedidoId == pedido).ToListAsync(),
            r => Assert.Equal(EstadoReservaProdutoAcabado.Ativa, r.Estado));
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(Guid.NewGuid(), variante.Id, 1))).Sucesso);

        Assert.Equal(TransicaoReservasStatus.Sucesso, await ciclo.ExpirarAsync(isolada.PedidoId));
        Assert.Equal(TransicaoReservasStatus.Repetida, await ciclo.ExpirarAsync(isolada.PedidoId));
        var expirada = await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync(r => r.Id == isolada.Id);
        Assert.Equal(EstadoReservaProdutoAcabado.Expirada, expirada.Estado);
        Assert.True(expirada.EncerradaEmUtc >= expirada.ExpiraEmUtc);
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(Guid.NewGuid(), variante.Id, 1))).Sucesso);
        Assert.Equal(3, (await context.SaldosProdutoAcabado.ToListAsync()).Sum(s => s.QuantidadeFisica));
    }

    [SqlServerIntegrationFact]
    public async Task Ciclo_ReverteAtualizacoesSePersistenciaFalharDepoisDeEnviarSql()
    {
        var interceptor = new FalharDepoisDoUpdateDeReserva();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: interceptor);
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var pedido = Guid.NewGuid();
        await EntrarAsync(context, variante.Id, 1);
        await EntrarAsync(context, variante.Id, 1);
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, variante.Id, 2))).Sucesso);

        var ciclo = database.Services.GetRequiredService<ICicloReservasProdutoAcabado>();
        await Assert.ThrowsAnyAsync<Exception>(() => ciclo.ConfirmarCompromissoAsync(pedido));
        Assert.True(interceptor.Executado);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(2, reservas.Count);
        Assert.All(reservas, r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Ativa, r.Estado);
            Assert.Null(r.ConfirmadaEmUtc);
        });
    }

    [SqlServerIntegrationFact]
    public async Task Ciclo_ConfirmaOuExpiraDeAcordoComPrazoEConcorreComLiberacao()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        await using var conexaoA = database.CreateVerificationContext();
        await using var conexaoB = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var lote = (await EntrarAsync(context, variante.Id, 3)).Lote!;
        var agora = await LerUtcSqlAsync(context);
        var vencida = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 1, agora.AddDays(-2));
        var vigente = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 1, agora);
        var cancelada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 1, agora);
        context.ReservasProdutoAcabado.AddRange(vencida, vigente, cancelada);
        await context.SaveChangesAsync();
        var cicloA = Repositorio(conexaoA);
        var cicloB = Repositorio(conexaoB);

        var (confirmarVencida, expirarVencida) = await DisputarAsync(
            () => cicloA.ConfirmarCompromissoAsync(vencida.PedidoId),
            () => cicloB.ExpirarAsync(vencida.PedidoId));
        Assert.Equal(TransicaoReservasStatus.Sucesso, expirarVencida);
        Assert.Contains(confirmarVencida, new[] { TransicaoReservasStatus.PrazoEncerrado, TransicaoReservasStatus.EstadoIncompativel });

        var (confirmarVigente, expirarVigente) = await DisputarAsync(
            () => cicloA.ConfirmarCompromissoAsync(vigente.PedidoId),
            () => cicloB.ExpirarAsync(vigente.PedidoId));
        Assert.Equal(TransicaoReservasStatus.Sucesso, confirmarVigente);
        Assert.Contains(expirarVigente, new[] { TransicaoReservasStatus.PrazoNaoEncerrado, TransicaoReservasStatus.EstadoIncompativel });

        var (confirmarCancelada, liberar) = await DisputarAsync(
            () => cicloA.ConfirmarCompromissoAsync(cancelada.PedidoId),
            () => cicloB.LiberarAsync(cancelada.PedidoId, "Cancelamento autorizado"));
        Assert.Equal(TransicaoReservasStatus.Sucesso, liberar);
        Assert.Contains(confirmarCancelada, new[] { TransicaoReservasStatus.Sucesso, TransicaoReservasStatus.EstadoIncompativel });
        var estados = await context.ReservasProdutoAcabado.AsNoTracking().ToDictionaryAsync(r => r.Id);
        Assert.Equal(EstadoReservaProdutoAcabado.Expirada, estados[vencida.Id].Estado);
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, estados[vigente.Id].Estado);
        Assert.Equal(EstadoReservaProdutoAcabado.Liberada, estados[cancelada.Id].Estado);
        Assert.Equal(confirmarCancelada == TransicaoReservasStatus.Sucesso,
            estados[cancelada.Id].ConfirmadaEmUtc.HasValue);
        Assert.Equal(3, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
    }

    [SqlServerIntegrationFact]
    public async Task Ciclo_ImpedeInclusaoDeVarianteDepoisDeConfirmarPedido()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var primeira = await CriarVarianteAsync(context);
        var segunda = await CriarVarianteAsync(context, "Velas");
        await EntrarAsync(context, primeira.Id, 2);
        await EntrarAsync(context, segunda.Id, 2);
        var pedido = Guid.NewGuid();
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, primeira.Id, 1))).Sucesso);
        await using var conexaoA = database.CreateVerificationContext();
        await using var conexaoB = database.CreateVerificationContext();

        var (confirmacao, alocacao) = await DisputarAsync(
            () => Repositorio(conexaoA).ConfirmarCompromissoAsync(pedido),
            () => CasoDeUso(conexaoB).ExecuteAsync(new(pedido, segunda.Id, 1)));
        Assert.Equal(TransicaoReservasStatus.Sucesso, confirmacao);
        Assert.Equal(alocacao.Sucesso ? ReservarProdutoAcabadoStatus.Sucesso : ReservarProdutoAcabadoStatus.PedidoForaDaAlocacao,
            alocacao.Status);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().Where(r => r.PedidoId == pedido).ToListAsync();
        Assert.Equal(alocacao.Sucesso ? 2 : 1, reservas.Count);
        Assert.Single(reservas.Select(r => r.ExpiraEmUtc).Distinct());
        Assert.All(reservas, r => Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado));
        Assert.True((await CasoDeUso(context).ExecuteAsync(new(pedido, primeira.Id, 1))).Repetida);
    }

    [SqlServerIntegrationFact]
    public async Task Ciclo_CompartilhaPrazoEExpiraIntegralmenteDuranteDisputaNoInstanteExato()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var primeira = await CriarVarianteAsync(context);
        var segunda = await CriarVarianteAsync(context, "Velas");
        var terceira = await CriarVarianteAsync(context, "Embalagens");
        await EntrarAsync(context, primeira.Id, 1);
        await EntrarAsync(context, segunda.Id, 1);
        await EntrarAsync(context, terceira.Id, 1);
        var pedido = Guid.NewGuid();
        var solicitacao = new ReservarProdutoAcabadoInput(pedido, primeira.Id, 1);
        await using var escopoA = database.Services.CreateAsyncScope();
        await using var escopoB = database.Services.CreateAsyncScope();
        await using var escopoC = database.Services.CreateAsyncScope();
        var alocar = escopoA.ServiceProvider.GetRequiredService<IReservarProdutoAcabado>();
        var expirar = escopoB.ServiceProvider.GetRequiredService<ICicloReservasProdutoAcabado>();
        var confirmar = escopoC.ServiceProvider.GetRequiredService<ICicloReservasProdutoAcabado>();

        var inicio = await LerUtcSqlAsync(context);
        relogio.Agora = inicio;
        var inicial = Assert.Single((await alocar.ExecuteAsync(solicitacao)).Reservas);
        var prazo = inicio.AddHours(24);
        relogio.Agora = inicio.AddHours(5);
        var adicional = Assert.Single((await alocar.ExecuteAsync(solicitacao with { ProdutoVarianteId = segunda.Id })).Reservas);
        Assert.Equal(inicio.AddHours(5), adicional.CriadaEmUtc);
        Assert.Equal(prazo, inicial.ExpiraEmUtc);
        Assert.Equal(prazo, adicional.ExpiraEmUtc);
        Assert.Equal(ReservarProdutoAcabadoStatus.SaldoInsuficiente,
            (await alocar.ExecuteAsync(solicitacao with { PedidoId = Guid.NewGuid(), ProdutoVarianteId = segunda.Id })).Status);

        relogio.Agora = prazo;
        Assert.Equal(ReservarProdutoAcabadoStatus.PedidoForaDaAlocacao,
            (await alocar.ExecuteAsync(solicitacao with { ProdutoVarianteId = terceira.Id })).Status);
        Assert.Equal(TransicaoReservasStatus.PrazoEncerrado, await confirmar.ConfirmarCompromissoAsync(pedido));
        var (inclusao, transicoes) = await DisputarAsync(
            () => alocar.ExecuteAsync(solicitacao with { ProdutoVarianteId = terceira.Id }),
            () => DisputarAsync(() => expirar.ExpirarAsync(pedido), () => confirmar.ConfirmarCompromissoAsync(pedido)));
        Assert.Equal(ReservarProdutoAcabadoStatus.PedidoForaDaAlocacao, inclusao.Status);
        Assert.Equal(TransicaoReservasStatus.Sucesso, transicoes.Item1);
        Assert.Contains(transicoes.Item2, new[] { TransicaoReservasStatus.PrazoEncerrado, TransicaoReservasStatus.EstadoIncompativel });
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().Where(r => r.PedidoId == pedido).ToListAsync();
        Assert.Equal(2, reservas.Count);
        Assert.All(reservas, r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Expirada, r.Estado);
            Assert.Equal(prazo, r.ExpiraEmUtc);
            Assert.Equal(prazo, r.EncerradaEmUtc);
        });
        Assert.Equal(TransicaoReservasStatus.Repetida, await expirar.ExpirarAsync(pedido));
        Assert.Equal(inicial.Id, Assert.Single((await alocar.ExecuteAsync(solicitacao)).Reservas).Id);
        Assert.True((await alocar.ExecuteAsync(solicitacao with { PedidoId = Guid.NewGuid() })).Sucesso);
        Assert.Equal(3, (await context.SaldosProdutoAcabado.ToListAsync()).Sum(s => s.QuantidadeFisica));
    }

    private static Task<DateTimeOffset> LerUtcSqlAsync(XaquiamuniDbContext context) =>
        context.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync();

    [SqlServerIntegrationFact]
    public async Task Ciclo_MigrationRecusaPrazosLegadosDivergentesSemReescreverReservas()
    {
        const string anterior = "20261118042020_PersistirReservasProdutoAcabado";
        const string atual = "20261118044605_CompartilharPrazoReservasPorPedido";
        await using var database = await SqlServerTestDatabase.CreateAsync(targetMigration: anterior);
        await using var context = database.CreateVerificationContext();
        var (_, variante, _) = await CriarProdutoCompravelAsync(context, categoriaAnteriorAVersao: true);
        var primeiroLote = LoteProdutoAcabado.Criar(variante, null, Hoje, Hoje);
        var segundoLote = LoteProdutoAcabado.Criar(variante, null, Hoje, Hoje);
        context.LotesProdutoAcabado.AddRange(primeiroLote, segundoLote);
        var pedido = Guid.NewGuid();
        var agora = await LerUtcSqlAsync(context);
        context.ReservasProdutoAcabado.AddRange(
            ReservaProdutoAcabado.Criar(pedido, variante.Id, primeiroLote.Id, 1, agora),
            ReservaProdutoAcabado.Criar(pedido, variante.Id, segundoLote.Id, 1, agora.AddHours(1)));
        await context.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync());
        Assert.Equal(51001, erro.Number);
        Assert.DoesNotContain(atual, await context.Database.GetAppliedMigrationsAsync());
        var prazos = await context.ReservasProdutoAcabado.AsNoTracking()
            .OrderBy(r => r.ExpiraEmUtc).Select(r => r.ExpiraEmUtc).ToListAsync();
        Assert.Equal([agora.AddHours(24), agora.AddHours(25)], prazos);
    }

    private sealed class FalharDepoisDoUpdateDeReserva : DbCommandInterceptor
    {
        public bool Executado { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [ReservasProdutoAcabado]", StringComparison.Ordinal))
            {
                Executado = true;
                await result.DisposeAsync();
                throw new InvalidOperationException("Falha de teste após atualizar reservas.");
            }

            return result;
        }
    }

    private sealed class RelogioSqlControlado : DbCommandInterceptor
    {
        private const string Relogio = "TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')";
        public DateTimeOffset? Agora { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Agora is DateTimeOffset agora && command.CommandText.Contains(Relogio, StringComparison.Ordinal))
            {
                var parametro = command.CreateParameter();
                parametro.ParameterName = "@agoraTeste";
                parametro.DbType = DbType.DateTimeOffset;
                parametro.Scale = 7;
                parametro.Value = agora;
                command.Parameters.Add(parametro);
                command.CommandText = command.CommandText.Replace(Relogio, "@agoraTeste", StringComparison.Ordinal);
            }

            return ValueTask.FromResult(result);
        }
    }
}
