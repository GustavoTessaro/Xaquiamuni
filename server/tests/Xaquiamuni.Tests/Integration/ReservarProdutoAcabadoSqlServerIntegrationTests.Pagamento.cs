using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task PagamentoConfirmaVariantesELotesComAuditoriaUnicaSemBaixa()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context, variosLotes: true);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var outroAdmin = await CriarAdministradorPagamentoAsync(context);
        var confirmar = database.Services.GetRequiredService<ConfirmarPagamentoPedido>();
        var antes = await LerUtcSqlAsync(context);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await confirmar.ExecuteAsync(pedido.Id, admin));
        var depois = await LerUtcSqlAsync(context);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Repetido, await confirmar.ExecuteAsync(pedido.Id, outroAdmin));

        var persistido = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(SituacaoPedido.PagamentoConfirmado, persistido.Situacao);
        Assert.Equal(admin, persistido.PagamentoConfirmadoPorId);
        var instante = Assert.IsType<DateTimeOffset>(persistido.PagamentoConfirmadoEmUtc);
        Assert.Equal(TimeSpan.Zero, instante.Offset);
        Assert.InRange(instante, antes, depois);
        Assert.Equal(pedido.PrazoPagamentoEmUtc, persistido.PrazoPagamentoEmUtc);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(ConsumirReservasStatus.EstadoIncompativel,
            (await Repositorio(context).ConsumirAsync(pedido.Id, admin)).Status);
        Assert.Equal(3, reservas.Count);
        Assert.Equal(2, reservas.Select(reserva => reserva.ProdutoVarianteId).Distinct().Count());
        Assert.All(reservas, reserva =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado);
            Assert.Equal(instante, reserva.ConfirmadaEmUtc);
            Assert.Equal(pedido.PrazoPagamentoEmUtc, reserva.ExpiraEmUtc);
        });
        Assert.Equal(3, (await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync()).Sum(saldo => saldo.QuantidadeFisica));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task PagamentoBloqueiaContaInaptaPedidoAusenteVencidoOuSemReservas()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, variante) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var bloqueado = await CriarAdministradorPagamentoAsync(context, bloqueado: true);
        var confirmar = ConfirmarPagamento(context);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.DadosInvalidos, await confirmar.ExecuteAsync(pedido.Id, Guid.Empty));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.AdministradorNaoAutorizado,
            await confirmar.ExecuteAsync(pedido.Id, Guid.NewGuid()));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.AdministradorNaoAutorizado,
            await confirmar.ExecuteAsync(pedido.Id, pedido.ClienteId));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.AdministradorNaoAutorizado,
            await confirmar.ExecuteAsync(pedido.Id, bloqueado));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.PedidoNaoEncontrado,
            await confirmar.ExecuteAsync(Guid.NewGuid(), admin));

        var item = new DadosItemPedido(variante.ProdutoId, variante.Id, "Sabonete", "Padrao", 1, 100);
        var agora = await LerUtcSqlAsync(context);
        var vencido = Pedido.Criar(pedido.ClienteId, agora.AddDays(-2), [item]);
        var semReservas = Pedido.Criar(pedido.ClienteId, agora.AddMinutes(-1), [item]);
        var lote = await context.LotesProdutoAcabado.AsNoTracking().SingleAsync();
        context.Pedidos.AddRange(vencido, semReservas);
        context.ReservasProdutoAcabado.Add(ReservaProdutoAcabado.Criar(
            vencido.Id, variante.Id, lote.Id, 1, vencido.CriadoEmUtc));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(ConfirmarPagamentoPedidoStatus.PrazoEncerrado, await confirmar.ExecuteAsync(vencido.Id, admin));
        Assert.Equal(ConfirmarPagamentoPedidoStatus.ReservasInconsistentes, await confirmar.ExecuteAsync(semReservas.Id, admin));

        var reserva = await context.ReservasProdutoAcabado.SingleAsync(r => r.PedidoId == pedido.Id);
        reserva.Liberar(await LerUtcSqlAsync(context), "teste");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(ConfirmarPagamentoPedidoStatus.ReservasInconsistentes, await confirmar.ExecuteAsync(pedido.Id, admin));
        Assert.All(await context.Pedidos.AsNoTracking().ToListAsync(), p =>
        {
            Assert.Equal(SituacaoPedido.AguardandoPagamento, p.Situacao);
            Assert.Null(p.PagamentoConfirmadoEmUtc);
        });
    }

    [SqlServerIntegrationFact]
    public async Task PagamentoReverteConfirmacaoJaGravadaDasReservasSePedidoFalhar()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(context.Database.GetConnectionString())
            .AddInterceptors(new FalhaAoGravarPagamento())
            .Options;
        await using var comFalha = new XaquiamuniDbContext(options);
        await Assert.ThrowsAsync<FalhaInjetada>(() => ConfirmarPagamento(comFalha).ExecuteAsync(pedido.Id, admin));
        Assert.Equal(SituacaoPedido.AguardandoPagamento, (await context.Pedidos.AsNoTracking().SingleAsync()).Situacao);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa,
            (await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await ConfirmarPagamento(context).ExecuteAsync(pedido.Id, admin));
    }

    [SqlServerIntegrationFact]
    public async Task PagamentoSerializaConfirmacoesEImpedeExpiracaoOuLiberacaoAvulsa()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var outroAdmin = await CriarAdministradorPagamentoAsync(context);
        await using var primeira = database.CreateVerificationContext();
        await using var segunda = database.CreateVerificationContext();
        await using var liberacao = database.CreateVerificationContext();
        await using var expiracao = database.CreateVerificationContext();
        var (confirmacoes, ciclo) = await DisputarAsync(
            () => DisputarAsync(() => ConfirmarPagamento(primeira).ExecuteAsync(pedido.Id, admin),
                () => ConfirmarPagamento(segunda).ExecuteAsync(pedido.Id, outroAdmin)),
            () => DisputarAsync(() => Repositorio(liberacao).LiberarAsync(pedido.Id, "teste"),
                () => Repositorio(expiracao).ExpirarAsync(pedido.Id)));

        Assert.Contains(ConfirmarPagamentoPedidoStatus.Confirmado, new[] { confirmacoes.Item1, confirmacoes.Item2 });
        Assert.Contains(ConfirmarPagamentoPedidoStatus.Repetido, new[] { confirmacoes.Item1, confirmacoes.Item2 });
        Assert.Equal((TransicaoReservasStatus.EstadoIncompativel, TransicaoReservasStatus.EstadoIncompativel), ciclo);
        var confirmado = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(confirmacoes.Item1 == ConfirmarPagamentoPedidoStatus.Confirmado ? admin : outroAdmin,
            confirmado.PagamentoConfirmadoPorId);
        Assert.Equal(confirmado.PagamentoConfirmadoEmUtc,
            (await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).ConfirmadaEmUtc);
        Assert.Equal(1, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task PagamentoRecusaNoLimiteExatoSemAlterarPedidoOuReserva()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var admin = await CriarAdministradorPagamentoAsync(context);
        var confirmar = database.Services.GetRequiredService<ConfirmarPagamentoPedido>();

        relogio.Agora = pedido.PrazoPagamentoEmUtc;
        Assert.Equal(ConfirmarPagamentoPedidoStatus.PrazoEncerrado, await confirmar.ExecuteAsync(pedido.Id, admin));
        Assert.Equal(SituacaoPedido.AguardandoPagamento, (await context.Pedidos.AsNoTracking().SingleAsync()).Situacao);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa,
            (await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).Estado);

        relogio.Agora = pedido.PrazoPagamentoEmUtc.AddTicks(-1);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Confirmado, await confirmar.ExecuteAsync(pedido.Id, admin));
        relogio.Agora = pedido.PrazoPagamentoEmUtc.AddDays(2);
        Assert.Equal(ConfirmarPagamentoPedidoStatus.Repetido, await confirmar.ExecuteAsync(pedido.Id, admin));
        var persistido = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(pedido.PrazoPagamentoEmUtc.AddTicks(-1), persistido.PagamentoConfirmadoEmUtc);
    }

    private static ConfirmarPagamentoPedido ConfirmarPagamento(XaquiamuniDbContext context) => new(Repositorio(context));

    private static async Task<Guid> CriarAdministradorPagamentoAsync(XaquiamuniDbContext context, bool bloqueado = false)
    {
        var role = await context.Roles.SingleOrDefaultAsync(papel => papel.NormalizedName == XaquiamuniRoles.Administrador);
        if (role is null)
        {
            role = new IdentityRole<Guid>(XaquiamuniRoles.Administrador)
            {
                Id = Guid.NewGuid(), NormalizedName = XaquiamuniRoles.Administrador,
            };
            context.Roles.Add(role);
        }

        var id = Guid.NewGuid();
        var usuario = new XaquiamuniUser
        {
            Id = id, UserName = id.ToString(), LockoutEnabled = true,
            LockoutEnd = bloqueado ? (await LerUtcSqlAsync(context)).AddHours(1) : null,
        };
        context.Users.Add(usuario);
        context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = role.Id });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return id;
    }

    private static async Task<(Pedido, ProdutoVariante)> CriarPedidoParaPagamentoAsync(
        XaquiamuniDbContext context, bool variosLotes = false)
    {
        var cliente = await CriarClienteCompraAsync(context);
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(context);
        await EntrarAsync(context, padrao.Id, 1);
        var itens = new List<ItemCompraInput> { new(produto.Id, padrao.Id, 1) };
        if (variosLotes)
        {
            await EntrarAsync(context, alternativa.Id, 1, validade: Hoje);
            await EntrarAsync(context, alternativa.Id, 1);
            itens.Add(new(produto.Id, alternativa.Id, 2));
        }
        var resultado = await Compra(context).ExecuteAsync(new(cliente, Guid.NewGuid(), itens));
        Assert.Equal(CriarPedidoCompraStatus.Sucesso, resultado.Status);
        return (Assert.IsType<Pedido>(resultado.Pedido), padrao);
    }

    private sealed class FalhaAoGravarPagamento : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<Pedido>().Any(entry => entry.State == EntityState.Modified))
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                Assert.All(context.ChangeTracker.Entries<ReservaProdutoAcabado>(), entry =>
                {
                    Assert.Equal(EntityState.Unchanged, entry.State);
                    Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, entry.Entity.Estado);
                });
                throw new FalhaInjetada();
            }

            return ValueTask.FromResult(result);
        }
    }
}
