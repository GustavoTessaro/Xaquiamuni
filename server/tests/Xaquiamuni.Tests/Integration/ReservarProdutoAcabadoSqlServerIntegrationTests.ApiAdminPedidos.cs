using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    private const string AdminPedidosUrl = AdminPedidosEndpoints.BasePath;

    [SqlServerIntegrationFact]
    public async Task ApiAdminPedidosProtegeTodasAsConsultasEComandosComAdminECsrf()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(context);
        var adminId = await CriarAdministradorPagamentoAsync(context);
        using var factory = CriarApiPedidos(database);
        using var anonimo = ClienteHttpPedidos(factory);
        using var cliente = ClienteHttpPedidos(factory, pedido.ClienteId);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var url = $"{AdminPedidosUrl}/{pedido.Id:D}";

        foreach (var (client, esperado) in new[]
        {
            (anonimo, HttpStatusCode.Unauthorized),
            (cliente, HttpStatusCode.Forbidden),
        })
        {
            using var lista = await client.GetAsync(AdminPedidosUrl);
            using var detalhe = await client.GetAsync(url);
            using var pagamento = await EnviarComandoAdminPedidoAsync(
                client, $"{url}/confirmar-pagamento", new { recebimentoConferido = true });
            using var cancelamento = await EnviarComandoAdminPedidoAsync(
                client, $"{url}/cancelar", new { motivo = "Solicitado pela cliente" });
            foreach (var resposta in new[] { lista, detalhe, pagamento, cancelamento })
            {
                Assert.Equal(esperado, resposta.StatusCode);
                Assert.True(resposta.Headers.CacheControl?.NoStore);
            }
        }

        var token = await CsrfPedidosAsync(admin);
        foreach (var csrf in new[] { null, "token-invalido" })
        {
            using var pagamento = await EnviarComandoAdminPedidoAsync(
                admin, $"{url}/confirmar-pagamento", new { recebimentoConferido = true }, csrf);
            using var cancelamento = await EnviarComandoAdminPedidoAsync(
                admin, $"{url}/cancelar", new { motivo = "teste" }, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, pagamento.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, cancelamento.StatusCode);
        }

        using var semConferencia = await EnviarComandoAdminPedidoAsync(
            admin, $"{url}/confirmar-pagamento", new { recebimentoConferido = false }, token);
        using var idInjetado = await EnviarComandoAdminPedidoAsync(admin, $"{url}/confirmar-pagamento",
            new { recebimentoConferido = true, adminId = Guid.NewGuid() }, token);
        using var idNoCancelamento = await EnviarComandoAdminPedidoAsync(admin, $"{url}/cancelar",
            new { motivo = "teste", administradorId = Guid.NewGuid() }, token);
        using var comprovante = await EnviarComandoAdminPedidoAsync(admin, $"{url}/confirmar-pagamento",
            new { recebimentoConferido = true, comprovante = "arquivo enviado" }, token);
        Assert.All(new[] { semConferencia, idInjetado, idNoCancelamento, comprovante },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(SituacaoPedido.AguardandoPagamento, await context.Pedidos.Select(p => p.Situacao).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa,
            await context.ReservasProdutoAcabado.Select(r => r.Estado).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ApiAdminPedidosListaFiltraEPaginaComContatoMinimoNoDetalhe()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, variante) = await CriarPedidoParaPagamentoAsync(context);
        var agora = await LerUtcSqlAsync(context);
        var lote = await context.LotesProdutoAcabado.AsNoTracking().SingleAsync();
        var segundo = CriarPedidoVencido(context, pedido, variante, lote.Id, agora.AddDays(-2));
        var terceiro = CriarPedidoVencido(context, pedido, variante, lote.Id, agora.AddDays(-2));
        var cliente = await context.Users.SingleAsync(usuario => usuario.Id == pedido.ClienteId);
        cliente.Email = "cliente-pedidos@example.test";
        cliente.PhoneNumber = "11999990000";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var adminId = await CriarAdministradorPagamentoAsync(context);
        Assert.Equal(Xaquiamuni.Aplicacao.Pedidos.ConfirmarPagamentoPedidoStatus.Confirmado,
            await ConfirmarPagamento(context).ExecuteAsync(pedido.Id, adminId));

        using var factory = CriarApiPedidos(database);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        using var resposta = await admin.GetAsync(AdminPedidosUrl);
        resposta.EnsureSuccessStatusCode();
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        using var json = await LerJsonPedidosAsync(resposta);
        var linhas = json.RootElement.GetProperty("itens").EnumerateArray().ToArray();
        var ids = linhas.Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(pedido.Id, ids[0]);
        Assert.Equal(new[] { segundo.Id, terceiro.Id }.Order(), ids.Skip(1).Order());
        Assert.Equal(3, json.RootElement.GetProperty("totalItens").GetInt32());
        Assert.Equal("Cliente de teste", linhas[0].GetProperty("nomeCliente").GetString());
        Assert.False(linhas[0].TryGetProperty("email", out _));
        Assert.Equal("1199", linhas[0].GetProperty("totalCentavos").GetString());

        for (var pagina = 1; pagina <= 3; pagina++)
        {
            using var respostaPagina = await admin.GetAsync($"{AdminPedidosUrl}?pagina={pagina}&itensPorPagina=1");
            respostaPagina.EnsureSuccessStatusCode();
            using var resultado = await LerJsonPedidosAsync(respostaPagina);
            Assert.Equal(3, resultado.RootElement.GetProperty("totalPaginas").GetInt32());
            Assert.Equal(ids[pagina - 1], Assert.Single(resultado.RootElement.GetProperty("itens").EnumerateArray())
                .GetProperty("id").GetGuid());
        }

        using var filtrada = await admin.GetAsync($"{AdminPedidosUrl}?situacao=PagamentoConfirmado");
        filtrada.EnsureSuccessStatusCode();
        using var filtro = await LerJsonPedidosAsync(filtrada);
        Assert.Equal(1, filtro.RootElement.GetProperty("totalItens").GetInt32());
        Assert.Equal(pedido.Id, Assert.Single(filtro.RootElement.GetProperty("itens").EnumerateArray())
            .GetProperty("id").GetGuid());
        using var filtroInvalido = await admin.GetAsync($"{AdminPedidosUrl}?situacao=2");
        using var paginaInvalida = await admin.GetAsync($"{AdminPedidosUrl}?itensPorPagina=51");
        Assert.Equal(HttpStatusCode.BadRequest, filtroInvalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, paginaInvalida.StatusCode);

        using var detalhe = await admin.GetAsync($"{AdminPedidosUrl}/{pedido.Id:D}");
        detalhe.EnsureSuccessStatusCode();
        using var detalheJson = await LerJsonPedidosAsync(detalhe);
        var raiz = detalheJson.RootElement;
        Assert.Equal(new[] { "cliente", "pedido" }, raiz.EnumerateObject().Select(p => p.Name).Order());
        var contato = raiz.GetProperty("cliente");
        Assert.Equal(new[] { "email", "nome" }, contato.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("cliente-pedidos@example.test", contato.GetProperty("email").GetString());
        var pedidoJson = raiz.GetProperty("pedido");
        Assert.Equal(new[] { "criadoEmUtc", "entrega", "freteCentavos", "id", "itens", "modalidadeEntrega",
            "prazoPagamentoEmUtc", "situacao", "subtotalProdutosCentavos", "totalCentavos" },
            pedidoJson.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("0", pedidoJson.GetProperty("freteCentavos").GetString());
        Assert.Equal("1199", pedidoJson.GetProperty("subtotalProdutosCentavos").GetString());
        Assert.Equal(JsonValueKind.Null, pedidoJson.GetProperty("entrega").ValueKind);
        var itemJson = Assert.Single(pedidoJson.GetProperty("itens").EnumerateArray());
        Assert.Equal(new[] { "nomeProduto", "nomeVariante", "precoUnitarioCentavos", "produtoId", "produtoVarianteId",
            "quantidade", "subtotalCentavos" }, itemJson.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(variante.Id, itemJson.GetProperty("produtoVarianteId").GetGuid());
        Assert.Equal("1199", itemJson.GetProperty("precoUnitarioCentavos").GetString());
        Assert.Equal(1, itemJson.GetProperty("quantidade").GetInt32());

        using var ausente = await admin.GetAsync($"{AdminPedidosUrl}/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task ApiAdminPedidosConfirmaECancelaComIdempotenciaEConflitosSemBaixa()
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        var context = database.Services.GetRequiredService<XaquiamuniDbContext>();
        var (pedido, variante) = await CriarPedidoParaPagamentoAsync(context, variosLotes: true);
        await EntrarAsync(context, variante.Id, 1);
        var compra = await Compra(context).ExecuteAsync(new(pedido.ClienteId, Guid.NewGuid(),
            [new(variante.ProdutoId, variante.Id, 1)]));
        var paraCancelar = Assert.IsType<Pedido>(compra.Pedido);
        var agora = await LerUtcSqlAsync(context);
        var loteId = await context.LotesProdutoAcabado.Where(lote => lote.ProdutoVarianteId == variante.Id)
            .Select(lote => lote.Id).FirstAsync();
        var vencido = CriarPedidoVencido(context, pedido, variante, loteId, agora.AddDays(-2));
        var item = new DadosItemPedido(variante.ProdutoId, variante.Id, "Sabonete", "Padrao", 1, 100);
        var inconsistente = Pedido.Criar(pedido.ClienteId, agora, [item]);
        context.Pedidos.Add(inconsistente);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var adminId = await CriarAdministradorPagamentoAsync(context);
        using var factory = CriarApiPedidos(database, relogio);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var token = await CsrfPedidosAsync(admin);

        var pagamentoUrl = $"{AdminPedidosUrl}/{pedido.Id:D}/confirmar-pagamento";
        var cancelamentoUrl = $"{AdminPedidosUrl}/{paraCancelar.Id:D}/cancelar";
        var pagamento = new { recebimentoConferido = true };
        using var pago = await EnviarComandoAdminPedidoAsync(admin, pagamentoUrl, pagamento, token);
        await VerificarResultadoAdminPedidoAsync(pago, pedido.Id, "PagamentoConfirmado", repetida: false);
        using var pagoRepetido = await EnviarComandoAdminPedidoAsync(admin, pagamentoUrl, pagamento, token);
        await VerificarResultadoAdminPedidoAsync(pagoRepetido, pedido.Id, "PagamentoConfirmado", repetida: true);

        using var semMotivo = await EnviarComandoAdminPedidoAsync(admin, cancelamentoUrl, new { motivo = " " }, token);
        Assert.Equal(HttpStatusCode.BadRequest, semMotivo.StatusCode);
        using var cancelado = await EnviarComandoAdminPedidoAsync(admin, cancelamentoUrl, new { motivo = "teste" }, token);
        await VerificarResultadoAdminPedidoAsync(cancelado, paraCancelar.Id, "Cancelado", repetida: false);
        using var canceladoRepetido = await EnviarComandoAdminPedidoAsync(admin, cancelamentoUrl, new { motivo = " teste " }, token);
        await VerificarResultadoAdminPedidoAsync(canceladoRepetido, paraCancelar.Id, "Cancelado", repetida: true);
        using var motivoDiferente = await EnviarComandoAdminPedidoAsync(admin, cancelamentoUrl, new { motivo = "outro" }, token);
        await VerificarConflitoAsync(motivoDiferente, "motivo_divergente");

        using var cancelarPago = await EnviarComandoAdminPedidoAsync(admin,
            $"{AdminPedidosUrl}/{pedido.Id:D}/cancelar", new { motivo = "teste" }, token);
        await VerificarConflitoAsync(cancelarPago, "estado_incompativel");
        using var confirmarCancelado = await EnviarComandoAdminPedidoAsync(admin,
            $"{AdminPedidosUrl}/{paraCancelar.Id:D}/confirmar-pagamento", pagamento, token);
        await VerificarConflitoAsync(confirmarCancelado, "estado_incompativel");
        using var expirado = await EnviarComandoAdminPedidoAsync(admin,
            $"{AdminPedidosUrl}/{vencido.Id:D}/confirmar-pagamento", pagamento, token);
        await VerificarConflitoAsync(expirado, "prazo_encerrado");
        using var semReservas = await EnviarComandoAdminPedidoAsync(admin,
            $"{AdminPedidosUrl}/{inconsistente.Id:D}/confirmar-pagamento", pagamento, token);
        await VerificarConflitoAsync(semReservas, "reservas_inconsistentes");
        using var ausente = await EnviarComandoAdminPedidoAsync(admin,
            $"{AdminPedidosUrl}/{Guid.NewGuid():D}/confirmar-pagamento", pagamento, token);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);

        var pagos = await context.ReservasProdutoAcabado.AsNoTracking().Where(r => r.PedidoId == pedido.Id).ToListAsync();
        Assert.Equal(3, pagos.Count);
        var dbPago = await context.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(adminId, dbPago.PagamentoConfirmadoPorId);
        Assert.All(pagos, r =>
        {
            Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, r.Estado);
            Assert.Equal(dbPago.PagamentoConfirmadoEmUtc, r.ConfirmadaEmUtc);
        });
        var dbCancelado = await context.Pedidos.AsNoTracking().SingleAsync(p => p.Id == paraCancelar.Id);
        var reservaCancelada = await context.ReservasProdutoAcabado.AsNoTracking().SingleAsync(r => r.PedidoId == paraCancelar.Id);
        Assert.Equal(adminId, dbCancelado.EncerradoPorId);
        Assert.Equal("teste", dbCancelado.MotivoCancelamento);
        Assert.Equal(EstadoReservaProdutoAcabado.Liberada, reservaCancelada.Estado);
        Assert.Equal(dbCancelado.EncerradoEmUtc, reservaCancelada.EncerradaEmUtc);
        Assert.Equal(4, (await context.SaldosProdutoAcabado.AsNoTracking().ToListAsync()).Sum(s => s.QuantidadeFisica));
        Assert.Equal(4, await context.MovimentosProdutoAcabado.CountAsync());
    }

    private static async Task<HttpResponseMessage> EnviarComandoAdminPedidoAsync(
        HttpClient client, string url, object body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType()) };
        if (token is not null)
        {
            request.Headers.Add(AntiforgeryConfiguration.HeaderName, token);
        }
        return await client.SendAsync(request);
    }

    private static async Task VerificarResultadoAdminPedidoAsync(
        HttpResponseMessage response, Guid id, string situacao, bool repetida)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = await LerJsonPedidosAsync(response);
        Assert.Equal(id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(situacao, json.RootElement.GetProperty("situacao").GetString());
        Assert.Equal(repetida, json.RootElement.GetProperty("repetida").GetBoolean());
    }
}
