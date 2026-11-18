using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task PreparacaoPedidoAvancaCadaEtapaUmaVezPreservandoReservasEEstoques()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context, variosLotes: true);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var outroAdmin = await CriarAdministradorPagamentoAsync(context);
        var preparar = database.Services.GetRequiredService<PrepararPedidoCompra>();
        Assert.Equal(PrepararPedidoStatus.EstadoIncompativel, await preparar.IniciarPreparacaoAsync(pedido.Id, admin));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await ConfirmarPagamento(context).ExecuteAsync(pedido.Id, admin));
        Assert.Equal(PrepararPedidoStatus.EstadoIncompativel, await preparar.LiberarParaRetiradaAsync(pedido.Id, admin));

        var instante = pedido.PrazoPagamentoEmUtc.AddHours(1);
        relogio.Agora = instante;
        Assert.Equal(PrepararPedidoStatus.Sucesso, await preparar.IniciarPreparacaoAsync(pedido.Id, admin));
        Assert.Equal(PrepararPedidoStatus.Repetida, await preparar.IniciarPreparacaoAsync(pedido.Id, outroAdmin));
        relogio.Agora = instante.AddHours(1);
        Assert.Equal(PrepararPedidoStatus.Sucesso, await preparar.LiberarParaRetiradaAsync(pedido.Id, outroAdmin));
        Assert.Equal(PrepararPedidoStatus.Repetida, await preparar.LiberarParaRetiradaAsync(pedido.Id, admin));
        Assert.Equal(PrepararPedidoStatus.EstadoIncompativel, await preparar.IniciarPreparacaoAsync(pedido.Id, admin));
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel, await Encerramento(context).ExpirarAsync(pedido.Id));
        Assert.Equal(EncerrarPedidoStatus.EstadoIncompativel, await Encerramento(context).CancelarAsync(pedido.Id, admin, "teste"));

        var atualizado = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, atualizado.Situacao);
        Assert.Equal(pedido.PrazoPagamentoEmUtc, atualizado.PrazoPagamentoEmUtc);
        Assert.Equal(admin, atualizado.PreparacaoIniciadaPorId);
        Assert.Equal(instante, atualizado.PreparacaoIniciadaEmUtc);
        Assert.Equal(outroAdmin, atualizado.RetiradaAutorizadaPorId);
        Assert.Equal(instante.AddHours(1), atualizado.RetiradaAutorizadaEmUtc);
        Assert.Equal(TimeSpan.Zero, atualizado.RetiradaAutorizadaEmUtc!.Value.Offset);
        Assert.Null(atualizado.EncerradoEmUtc);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(3, reservas.Count);
        Assert.Equal(2, reservas.Select(r => r.ProdutoVarianteId).Distinct().Count());
        Assert.All(reservas, r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado);
            Assert.Equal(atualizado.PagamentoConfirmadoEmUtc, r.ConfirmadaEmUtc);
            Assert.Equal(pedido.PrazoPagamentoEmUtc, r.ExpiraEmUtc);
        });
        Assert.Equal(3, (await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync()).Sum(s => s.QuantidadeFisica));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task PreparacaoPedidoRecusaAcessoInvalidoEReservasAusentesNaLiberacao()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var bloqueado = await CriarAdministradorPagamentoAsync(context, bloqueado: true);
        var preparar = new PrepararPedidoCompra(Repositorio(context));
        Assert.Equal(PrepararPedidoStatus.DadosInvalidos, await preparar.IniciarPreparacaoAsync(Guid.Empty, admin));
        Assert.Equal(PrepararPedidoStatus.PedidoNaoEncontrado, await preparar.IniciarPreparacaoAsync(Guid.NewGuid(), admin));
        Assert.Equal(PrepararPedidoStatus.AdministradorNaoAutorizado, await preparar.IniciarPreparacaoAsync(pedido.Id, Guid.NewGuid()));
        Assert.Equal(PrepararPedidoStatus.AdministradorNaoAutorizado, await preparar.IniciarPreparacaoAsync(pedido.Id, pedido.ClienteId));
        Assert.Equal(PrepararPedidoStatus.AdministradorNaoAutorizado, await preparar.LiberarParaRetiradaAsync(pedido.Id, bloqueado));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await ConfirmarPagamento(context).ExecuteAsync(pedido.Id, admin));
        Assert.Equal(PrepararPedidoStatus.Sucesso, await preparar.IniciarPreparacaoAsync(pedido.Id, admin));
        context.ReservasProdutoAcabado.Remove(await context.ReservasProdutoAcabado.SingleAsync());
        await context.SaveChangesAsync();
        Assert.Equal(PrepararPedidoStatus.ReservasInconsistentes, await preparar.LiberarParaRetiradaAsync(pedido.Id, admin));
        var persistido = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(SituacaoPedido.EmPreparacao, persistido.Situacao);
        Assert.Null(persistido.RetiradaAutorizadaEmUtc);
    }

    [SqlServerIntegrationFact]
    public async Task PreparacaoPedidoReverteAposEscritaEProtegeRequisicoesSimultaneas()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await ConfirmarPagamento(context).ExecuteAsync(pedido.Id, admin));
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(context.Database.GetConnectionString()).AddInterceptors(new FalhaAposAtualizarPreparacao()).Options;
        await using var comFalha = new XaquiamuniDbContext(options);
        await Assert.ThrowsAsync<FalhaInjetada>(() =>
            new PrepararPedidoCompra(Repositorio(comFalha)).IniciarPreparacaoAsync(pedido.Id, admin));
        var original = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(SituacaoPedido.PagamentoConfirmado, original.Situacao);
        Assert.Null(original.PreparacaoIniciadaEmUtc);

        await using var outra = database.CreateVerificationContext();
        var a = new PrepararPedidoCompra(Repositorio(context));
        var b = new PrepararPedidoCompra(Repositorio(outra));
        var preparacoes = await DisputarAsync(() => a.IniciarPreparacaoAsync(pedido.Id, admin), () => b.IniciarPreparacaoAsync(pedido.Id, admin));
        var retiradas = await DisputarAsync(() => a.LiberarParaRetiradaAsync(pedido.Id, admin), () => b.LiberarParaRetiradaAsync(pedido.Id, admin));
        foreach (var (primeiro, segundo) in new[] { preparacoes, retiradas })
        {
            Assert.Equal(new[] { PrepararPedidoStatus.Sucesso, PrepararPedidoStatus.Repetida }, new[] { primeiro, segundo }.Order());
        }
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, (await context.Pedidos.AsNoTracking().SingleAsync()).Situacao);
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, (await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    private sealed class FalhaAposAtualizarPreparacao : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, result);
            Assert.NotNull(eventData.Context!.Database.CurrentTransaction);
            Assert.Equal(SituacaoPedido.EmPreparacao, Assert.Single(eventData.Context.ChangeTracker.Entries<Pedido>()).Entity.Situacao);
            throw new FalhaInjetada();
        }
    }
}
