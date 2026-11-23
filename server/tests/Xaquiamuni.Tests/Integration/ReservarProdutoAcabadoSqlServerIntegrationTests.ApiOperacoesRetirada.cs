using System.Net;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ApiOperacoesRetiradaProtegeCadaRotaERejeitaCamposIndevidos()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, adminId) = await CriarPedidoProntoAsync(context);
        using var factory = CriarApiPedidos(database);
        using var anonimo = ClienteHttpPedidos(factory);
        using var cliente = ClienteHttpPedidos(factory, pedido.ClienteId);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var token = await CsrfPedidosAsync(admin);
        var baseUrl = $"{AdminPedidosUrl}/{pedido.Id:D}";
        foreach (var (rota, corpo) in new (string, object)[]
        {
            ("iniciar-preparacao", new { }),
            ("liberar-retirada", new { }),
            ("confirmar-entrega", new { entregaPresencialConfirmada = true }),
        })
        {
            var url = $"{baseUrl}/{rota}";
            using var semSessao = await EnviarComandoAdminPedidoAsync(anonimo, url, corpo);
            using var semPapel = await EnviarComandoAdminPedidoAsync(cliente, url, corpo);
            using var semCsrf = await EnviarComandoAdminPedidoAsync(admin, url, corpo);
            using var csrfInvalido = await EnviarComandoAdminPedidoAsync(admin, url, corpo, "invalido");
            using var campoIndevido = await EnviarComandoAdminPedidoAsync(admin, url, new { adminId }, token);
            Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, semPapel.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, csrfInvalido.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, campoIndevido.StatusCode);
        }

        foreach (var corpo in new object[] { new { }, new { entregaPresencialConfirmada = false },
            new { entregaPresencialConfirmada = true, quantidade = 9 }, new { entregaPresencialConfirmada = true, adminId } })
        {
            using var resposta = await EnviarComandoAdminPedidoAsync(admin, $"{baseUrl}/confirmar-entrega", corpo, token);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }

        using var ausente = await EnviarComandoAdminPedidoAsync(admin,
            $"{AdminPedidosUrl}/{Guid.NewGuid():D}/iniciar-preparacao", new { }, token);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, await context.Pedidos.Select(p => p.Situacao).SingleAsync());
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ApiOperacoesRetiradaRespeitaEtapasEEntregaEmConcorrenciaUmaVez()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context, variosLotes: true);
        var adminId = await CriarAdministradorPagamentoAsync(context);
        var outroAdminId = await CriarAdministradorPagamentoAsync(context);
        using var factory = CriarApiPedidos(database);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        using var outroAdmin = ClienteHttpPedidos(factory, outroAdminId, XaquiamuniRoles.Administrador);
        var token = await CsrfPedidosAsync(admin);
        var outroToken = await CsrfPedidosAsync(outroAdmin);
        var url = $"{AdminPedidosUrl}/{pedido.Id:D}";
        using var naoPago = await EnviarComandoAdminPedidoAsync(admin, $"{url}/iniciar-preparacao", new { }, token);
        await VerificarConflitoAsync(naoPago, "estado_incompativel");
        using var pago = await EnviarComandoAdminPedidoAsync(admin, $"{url}/confirmar-pagamento",
            new { recebimentoConferido = true }, token);
        await VerificarResultadoAdminPedidoAsync(pago, pedido.Id, "PagamentoConfirmado", false);
        using var salto = await EnviarComandoAdminPedidoAsync(admin, $"{url}/liberar-retirada", new { }, token);
        await VerificarConflitoAsync(salto, "estado_incompativel");

        foreach (var (rota, situacao) in new[] { ("iniciar-preparacao", "EmPreparacao"), ("liberar-retirada", "ProntoParaRetirada") })
        {
            using var primeira = await EnviarComandoAdminPedidoAsync(admin, $"{url}/{rota}", new { }, token);
            await VerificarResultadoAdminPedidoAsync(primeira, pedido.Id, situacao, false);
            using var repetida = await EnviarComandoAdminPedidoAsync(outroAdmin, $"{url}/{rota}", new { }, outroToken);
            await VerificarResultadoAdminPedidoAsync(repetida, pedido.Id, situacao, true);
            Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
        }

        using var detalhe = await admin.GetAsync(url);
        detalhe.EnsureSuccessStatusCode();
        using var entregaPropria = new HttpRequestMessage(HttpMethod.Post, $"{url}/iniciar-entrega-propria")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { confirmado = true }),
        };
        entregaPropria.Headers.Add("X-XSRF-TOKEN", token);
        entregaPropria.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        entregaPropria.Headers.IfMatch.Add(detalhe.Headers.ETag!);
        using var modalidadeInvalida = await admin.SendAsync(entregaPropria);
        await VerificarConflitoAsync(modalidadeInvalida, "estado_incompativel");

        var corpo = new { entregaPresencialConfirmada = true };
        var (a, b) = await DisputarAsync(
            () => EnviarComandoAdminPedidoAsync(admin, $"{url}/confirmar-entrega", corpo, token),
            () => EnviarComandoAdminPedidoAsync(outroAdmin, $"{url}/confirmar-entrega", corpo, outroToken));
        using var primeiraEntrega = a;
        using var segundaEntrega = b;
        using var jsonA = await LerJsonPedidosAsync(a);
        var aRepetida = jsonA.RootElement.GetProperty("repetida").GetBoolean();
        Assert.Equal(new[] { "id", "repetida", "situacao" }, jsonA.RootElement.EnumerateObject().Select(p => p.Name).Order());
        await VerificarResultadoAdminPedidoAsync(a, pedido.Id, "Entregue", aRepetida);
        await VerificarResultadoAdminPedidoAsync(b, pedido.Id, "Entregue", !aRepetida);

        var persistido = await context.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(adminId, persistido.PreparacaoIniciadaPorId);
        Assert.Equal(adminId, persistido.RetiradaAutorizadaPorId);
        Assert.Equal(aRepetida ? outroAdminId : adminId, persistido.EncerradoPorId);
        Assert.Equal(TimeSpan.Zero, persistido.EncerradoEmUtc!.Value.Offset);
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync(m => m.ReservaProdutoAcabadoId != null));
        Assert.All(await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync(), saldo => Assert.Equal(0, saldo.QuantidadeFisica));
        Assert.All(await context.ReservasProdutoAcabado.AsNoTracking().ToListAsync(), reserva =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Consumida, reserva.Estado);
            Assert.Equal(persistido.EncerradoEmUtc, reserva.EncerradaEmUtc);
        });
    }

    [SqlServerIntegrationFact]
    public async Task ApiOperacoesRetiradaDistingueInelegibilidadeSaldoEReservasInconsistentes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, adminId) = await CriarPedidoProntoAsync(context, variosLotes: true);
        using var factory = CriarApiPedidos(database);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var token = await CsrfPedidosAsync(admin);
        var url = $"{AdminPedidosUrl}/{pedido.Id:D}/confirmar-entrega";
        var corpo = new { entregaPresencialConfirmada = true };

        var produto = await context.Produtos.Include(p => p.Variantes).SingleAsync();
        var variante = produto.Variantes.Single(v => !v.Padrao);
        produto.DesativarVariante(variante.Id);
        await context.SaveChangesAsync();
        using var inelegivel = await EnviarComandoAdminPedidoAsync(admin, url, corpo, token);
        await VerificarConflitoAsync(inelegivel, "lote_inelegivel");

        produto.AtivarVariante(variante.Id);
        await context.SaveChangesAsync();
        var reserva = await context.ReservasProdutoAcabado.FirstAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [SaldosProdutoAcabado] SET [QuantidadeVendavel] = 0 WHERE [LoteId] = {reserva.LoteId}");
        using var insuficiente = await EnviarComandoAdminPedidoAsync(admin, url, corpo, token);
        await VerificarConflitoAsync(insuficiente, "saldo_insuficiente");

        context.ReservasProdutoAcabado.Remove(reserva);
        await context.SaveChangesAsync();
        using var inconsistente = await EnviarComandoAdminPedidoAsync(admin, url, corpo, token);
        await VerificarConflitoAsync(inconsistente, "reservas_inconsistentes");
        Assert.Equal(SituacaoPedido.ProntoParaRetirada, await context.Pedidos.Select(p => p.Situacao).SingleAsync());
        Assert.Equal(3, await context.MovimentosProdutoAcabado.CountAsync());
    }
}
