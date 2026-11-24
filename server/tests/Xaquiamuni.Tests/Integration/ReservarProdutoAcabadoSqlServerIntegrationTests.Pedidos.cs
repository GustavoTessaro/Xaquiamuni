using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task CriarPedidoUsaPrecosDoCatalogoFefoEPrazoComum()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Assert.NotNull(database.Services.GetRequiredService<CriarPedidoCompra>());
        await using var context = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(context);
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(context);
        var posterior = (await EntrarAsync(context, alternativa.Id, 4)).Lote!;
        var prioritario = (await EntrarAsync(context, alternativa.Id, 1, validade: Hoje)).Lote!;
        var unico = (await EntrarAsync(context, padrao.Id, 1)).Lote!;
        var input = new CriarPedidoCompraInput(cliente, Guid.NewGuid(),
            [new(produto.Id, alternativa.Id, 2), new(produto.Id, padrao.Id, 1)]);

        var resultado = await Compra(context).ExecuteAsync(input);
        Assert.Equal(CriarPedidoCompraStatus.Sucesso, resultado.Status);
        var pedido = Assert.IsType<Pedido>(resultado.Pedido);
        Assert.Equal(SituacaoPedido.AguardandoPagamento, pedido.Situacao);
        Assert.Equal(cliente, pedido.ClienteId);
        Assert.Equal(3201, pedido.TotalCentavos);
        var linha = Assert.Single(pedido.Itens, item => item.ProdutoVarianteId == alternativa.Id);
        Assert.Equal(1001, linha.PrecoUnitarioCentavos);
        Assert.Equal("Sabonete", linha.NomeProduto);
        Assert.Equal("Lavanda", linha.NomeVariante);
        Assert.Equal(pedido.CriadoEmUtc.AddHours(24), pedido.PrazoPagamentoEmUtc);
        var reservas = await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync();
        Assert.Equal(3, reservas.Count);
        Assert.Equal(new[] { posterior.Id, prioritario.Id, unico.Id }.Order(),
            reservas.Select(reserva => reserva.LoteId).Order());
        Assert.All(reservas, reserva =>
        {
            Assert.Equal(1, reserva.Quantidade);
            Assert.Equal(pedido.Id, reserva.PedidoId);
            Assert.Equal(pedido.CriadoEmUtc, reserva.CriadaEmUtc);
            Assert.Equal(pedido.PrazoPagamentoEmUtc, reserva.ExpiraEmUtc);
        });
        Assert.Equal(6, await context.SaldosProdutoAcabado.SumAsync(saldo => saldo.QuantidadeVendavel));
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());

        alternativa.AtualizarDados("Novo nome", "UN", 20m);
        context.Attach(alternativa).Property(item => item.PrecoVenda).IsModified = true;
        await context.SaveChangesAsync();
        var replay = await Compra(context).ExecuteAsync(input with
        {
            Itens = [new(produto.Id, padrao.Id, 1), new(produto.Id, alternativa.Id, 1), new(produto.Id, alternativa.Id, 1)],
        });
        Assert.True(replay.Repetida);
        Assert.Equal(pedido.Id, replay.Pedido!.Id);
        Assert.Equal(3201, replay.Pedido.TotalCentavos);
        Assert.Equal(CriarPedidoCompraStatus.ConflitoIdempotencia, (await Compra(context).ExecuteAsync(
            input with { Itens = [new(produto.Id, padrao.Id, 1)] })).Status);
        var outroCliente = await CriarClienteCompraAsync(context);
        var independente = await Compra(context).ExecuteAsync(input with
        {
            ClienteId = outroCliente,
            Itens = [new(produto.Id, alternativa.Id, 1)],
        });
        Assert.Equal(CriarPedidoCompraStatus.Sucesso, independente.Status);
        Assert.Equal(outroCliente, independente.Pedido!.ClienteId);
        Assert.NotEqual(pedido.Id, independente.Pedido.Id);
        Assert.Equal(2000, independente.Pedido.TotalCentavos);
    }

    [SqlServerIntegrationFact]
    public async Task CriarPedidoRejeitaClienteCatalogoQuantidadeELimiteMonetarioInvalidos()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(context, preco: 9999999999999999.99m);
        var outro = Produto.CriarSimples("Oculto", null, produto.Categoria, "UN", 1m);
        context.Attach(produto.Categoria);
        context.Produtos.Add(outro);
        await context.SaveChangesAsync();
        var cliente = await CriarClienteCompraAsync(context);
        var semEmail = await CriarClienteCompraAsync(context, confirmarEmail: false);
        var item = new ItemCompraInput(produto.Id, padrao.Id, 1);
        var input = new CriarPedidoCompraInput(cliente, Guid.NewGuid(), [item]);
        var caso = Compra(context);
        Assert.Equal(CriarPedidoCompraStatus.ClienteInapto, (await caso.ExecuteAsync(input with { ClienteId = Guid.NewGuid() })).Status);
        Assert.Equal(CriarPedidoCompraStatus.ClienteInapto, (await caso.ExecuteAsync(input with { ClienteId = semEmail })).Status);
        Assert.Equal(CriarPedidoCompraStatus.DadosInvalidos, (await caso.ExecuteAsync(input with { Itens = [item with { Quantidade = 0 }] })).Status);
        Assert.Equal(CriarPedidoCompraStatus.ItemNaoCompravel, (await caso.ExecuteAsync(input with { Itens = [item with { ProdutoId = outro.Id }] })).Status);
        Assert.Equal(CriarPedidoCompraStatus.ItemNaoCompravel, (await caso.ExecuteAsync(input with
        {
            Itens = [new(outro.Id, Assert.Single(outro.Variantes).Id, 1)],
        })).Status);
        Assert.Equal(CriarPedidoCompraStatus.LimiteMonetarioExcedido, (await caso.ExecuteAsync(
            input with { Itens = [item with { Quantidade = 10 }] })).Status);
        var produtoRastreado = await context.Produtos.Include(p => p.Variantes).SingleAsync(p => p.Id == produto.Id);
        produtoRastreado.DesativarVariante(alternativa.Id);
        await context.SaveChangesAsync();
        Assert.Equal(CriarPedidoCompraStatus.ItemNaoCompravel, (await caso.ExecuteAsync(
            input with { Itens = [item with { ProdutoVarianteId = alternativa.Id }] })).Status);
        Assert.Empty(await context.Pedidos.ToListAsync());
        Assert.Empty(await context.ReservasProdutoAcabado.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CriarPedidoReverteCarrinhoParcialEFalhaDepoisDaGravacaoDoPedido()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(context);
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(context);
        var variantes = new[] { padrao, alternativa }.OrderBy(item => item.Id).ToArray();
        await EntrarAsync(context, variantes[0].Id, 1);
        var input = new CriarPedidoCompraInput(cliente, Guid.NewGuid(),
            [new(produto.Id, variantes[0].Id, 1), new(produto.Id, variantes[1].Id, 1)]);
        Assert.Equal(CriarPedidoCompraStatus.SaldoInsuficiente, (await Compra(context).ExecuteAsync(input)).Status);
        Assert.Empty(await context.Pedidos.ToListAsync());
        Assert.Empty(await context.ReservasProdutoAcabado.ToListAsync());

        await EntrarAsync(context, variantes[1].Id, 1);
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(context.Database.GetConnectionString())
            .AddInterceptors(new FalhaAntesDeGravarReservas())
            .Options;
        await using var falha = new XaquiamuniDbContext(options);
        await Assert.ThrowsAsync<FalhaInjetada>(() => Compra(falha).ExecuteAsync(input));
        Assert.Empty(await context.Pedidos.ToListAsync());
        Assert.Empty(await context.ItensPedido.ToListAsync());
        Assert.Empty(await context.ReservasProdutoAcabado.ToListAsync());
        Assert.Equal(2, await context.SaldosProdutoAcabado.SumAsync(saldo => saldo.QuantidadeVendavel));
        var retry = await Compra(context).ExecuteAsync(input);
        Assert.Equal(CriarPedidoCompraStatus.Sucesso, retry.Status);
        Assert.False(retry.Repetida);
    }

    [SqlServerIntegrationFact]
    public async Task CriarPedidoConcorrenteProtegeUltimaUnidadeERetornaReplayPelaChave()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(context);
        var (produto, padrao, _) = await CriarProdutoCompravelAsync(context);
        await EntrarAsync(context, padrao.Id, 1);
        var input = new CriarPedidoCompraInput(cliente, Guid.NewGuid(), [new(produto.Id, padrao.Id, 1)]);
        var outroInput = input with { ChaveIdempotencia = Guid.NewGuid() };
        await using var primeira = database.CreateVerificationContext();
        await using var segunda = database.CreateVerificationContext();
        await using var terceira = database.CreateVerificationContext();

        var ((a, repeticao), b) = await DisputarAsync(
            () => DisputarAsync(() => Compra(primeira).ExecuteAsync(input), () => Compra(segunda).ExecuteAsync(input)),
            () => Compra(terceira).ExecuteAsync(outroInput));

        var vencedores = new[] { a, repeticao, b }.Where(resultado => resultado.Status == CriarPedidoCompraStatus.Sucesso).ToArray();
        var vencedor = Assert.Single(vencedores, resultado => !resultado.Repetida);
        Assert.All(vencedores, resultado => Assert.Equal(vencedor.Pedido!.Id, resultado.Pedido!.Id));
        if (b.Status == CriarPedidoCompraStatus.Sucesso)
        {
            Assert.Equal(CriarPedidoCompraStatus.SaldoInsuficiente, a.Status);
            Assert.Equal(CriarPedidoCompraStatus.SaldoInsuficiente, repeticao.Status);
            var (replayA, replayB) = await DisputarAsync(
                () => Compra(primeira).ExecuteAsync(outroInput), () => Compra(segunda).ExecuteAsync(outroInput));
            Assert.All(new[] { replayA, replayB }, replay => Assert.True(replay.Repetida));
        }
        else
        {
            Assert.Equal(CriarPedidoCompraStatus.SaldoInsuficiente, b.Status);
            Assert.Single(vencedores, resultado => resultado.Repetida);
        }

        Assert.Equal(1, await context.Pedidos.CountAsync());
        Assert.Equal(1, await context.ItensPedido.CountAsync());
        Assert.Equal(1, await context.ReservasProdutoAcabado.CountAsync());
        Assert.Equal(1, await context.SaldosProdutoAcabado.SumAsync(saldo => saldo.QuantidadeVendavel));
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CriarPedidoConcorreComSaidaAdministrativaPeloMesmoSaldo()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(context);
        var (produto, padrao, _) = await CriarProdutoCompravelAsync(context);
        var lote = (await EntrarAsync(context, padrao.Id, 1)).Lote!;
        var input = new CriarPedidoCompraInput(cliente, Guid.NewGuid(), [new(produto.Id, padrao.Id, 1)]);
        await using var movimentoContext = database.CreateVerificationContext();
        var (compra, movimento) = await DisputarAsync(
            () => Compra(context).ExecuteAsync(input),
            () => Repositorio(movimentoContext).RegistrarOperacaoAsync(
                Operacao(padrao.Id, lote.Id, TipoMovimentoProdutoAcabado.SaidaManual, 1)));

        var comprou = compra.Status == CriarPedidoCompraStatus.Sucesso;
        Assert.NotEqual(comprou, movimento.Sucesso);
        Assert.Equal(comprou ? 1 : 0, await context.ReservasProdutoAcabado.CountAsync());
        Assert.Equal(comprou ? 1 : 0, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(comprou ? 1 : 2, await context.MovimentosProdutoAcabado.CountAsync());
    }

    private static CriarPedidoCompra Compra(XaquiamuniDbContext context) => new(Repositorio(context));

    private static async Task<Guid> CriarClienteCompraAsync(XaquiamuniDbContext context, bool confirmarEmail = true)
    {
        var id = Guid.NewGuid();
        var usuario = new XaquiamuniUser { Id = id, UserName = id.ToString(), EmailConfirmed = confirmarEmail };
        var cpf = new CpfProtegido(Convert.ToBase64String(new byte[11]), Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]), (id.ToString("N") + id.ToString("N")).ToUpperInvariant(), "teste", "teste");
        context.Users.Add(usuario);
        context.ClientePerfis.Add(ClientePerfil.Criar(id, "Cliente de teste", cpf));
        var role = await context.Roles.SingleAsync(papel => papel.NormalizedName == XaquiamuniRoles.Cliente);
        context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = role.Id });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return id;
    }

    internal static async Task<(Produto Produto, ProdutoVariante Padrao, ProdutoVariante Alternativa)> CriarProdutoCompravelAsync(
        XaquiamuniDbContext context, decimal preco = 11.99m, bool categoriaAnteriorAVersao = false)
    {
        var categoria = Categoria.Criar("Artesanais", null, 0);
        if (categoriaAnteriorAVersao)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Categorias] ([Id], [Nome], [NomeNormalizado], [Ativa], [OrdemExibicao])
                VALUES ({categoria.Id}, {categoria.Nome}, {categoria.NomeNormalizado}, {categoria.Ativa}, {categoria.OrdemExibicao})
                """);
            context.Attach(categoria);
        }
        var padrao = ProdutoVariante.Criar("Padrao", "UN", preco, padrao: true);
        var alternativa = ProdutoVariante.Criar("Lavanda", "UN", 10.01m);
        var produto = Produto.Criar("Sabonete", null, categoria, [padrao, alternativa]);
        produto.AdicionarImagem(ChaveArmazenamentoImagemProduto.Gerar(), "foto.webp", "image/webp", 45, 64, 64);
        produto.Publicar();
        context.Produtos.Add(produto);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (produto, padrao, alternativa);
    }

    private sealed class FalhaInjetada : Exception;

    private sealed class FalhaAntesDeGravarReservas : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<ReservaProdutoAcabado>().Any(entry => entry.State == EntityState.Added))
            {
                Assert.Equal(EntityState.Unchanged, Assert.Single(context.ChangeTracker.Entries<Pedido>()).State);
                Assert.NotNull(context.Database.CurrentTransaction);
                throw new FalhaInjetada();
            }

            return ValueTask.FromResult(result);
        }
    }
}
