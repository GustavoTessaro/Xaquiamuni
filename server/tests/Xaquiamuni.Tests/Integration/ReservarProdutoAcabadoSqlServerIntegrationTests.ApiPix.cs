using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public Task ApiPixAutorizaConfiguracaoEExplicaAusenciaOuEncerramentoSemAlterarPedido() =>
        VerificarApiPixAsync(new("Email", "recebimento@example.test", "Loja de teste", null),
            "loja@example.test", "loja@example.test");

    [SqlServerIntegrationFact]
    public Task ApiPixCnpjNormalizaEPreservaAutorizacaoCsrfETitularidade() =>
        VerificarApiPixAsync(new("Cnpj", "99.999.999/9999-62", "Loja de teste", null),
            "12.abc.345/01de-35", "12ABC34501DE35");

    private static async Task VerificarApiPixAsync(SalvarPixManualRequest dto, string novaChave, string chaveNormalizada)
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, variante) = await CriarPedidoParaPagamentoAsync(context);
        var adminId = await CriarAdministradorPagamentoAsync(context);
        var outroId = await CriarClienteCompraAsync(context);
        var agora = await LerUtcSqlAsync(context);
        var item = new DadosItemPedido(variante.ProdutoId, variante.Id, "Sabonete", "Padrao", 1, 1390);
        var pago = Pedido.Criar(pedido.ClienteId, agora, [item]);
        pago.ConfirmarPagamento(adminId, agora);
        var cancelado = Pedido.Criar(pedido.ClienteId, agora, [item]);
        cancelado.Cancelar(adminId, agora, "teste");
        var vencido = Pedido.Criar(pedido.ClienteId, agora.AddDays(-2), [item]);
        var expirado = Pedido.Criar(pedido.ClienteId, agora.AddDays(-2), [item]);
        expirado.Expirar(agora);
        var entregue = Pedido.Criar(pedido.ClienteId, agora, [item]);
        entregue.ConfirmarPagamento(adminId, agora);
        entregue.IniciarPreparacao(adminId, agora);
        entregue.AutorizarRetirada(adminId, agora);
        entregue.ConfirmarEntrega(adminId, agora);
        context.Pedidos.AddRange(pago, cancelado, vencido, expirado, entregue);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        using var factory = CriarApiPedidos(database);
        using var anonimo = ClienteHttpPedidos(factory);
        using var cliente = ClienteHttpPedidos(factory, pedido.ClienteId);
        using var outro = ClienteHttpPedidos(factory, outroId);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var url = $"/api/pedidos/{pedido.Id:D}/pagamento";
        using var semSessao = await anonimo.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        using var alheio = await outro.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        using var ausente = await cliente.GetAsync(url);
        using var semConfig = await LerJsonPedidosAsync(ausente);
        Assert.Equal(HttpStatusCode.OK, ausente.StatusCode);
        Assert.Equal("configuracao_ausente", semConfig.RootElement.GetProperty("situacao").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, semConfig.RootElement.GetProperty("pix").ValueKind);

        using var anonimoPut = await SalvarPixAsync(anonimo, dto);
        using var clientePut = await SalvarPixAsync(cliente, dto);
        using var semCsrf = await SalvarPixAsync(admin, dto);
        using var csrfInvalido = await SalvarPixAsync(admin, dto, "invalido");
        Assert.Equal(HttpStatusCode.Unauthorized, anonimoPut.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clientePut.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, csrfInvalido.StatusCode);

        var token = await CsrfPedidosAsync(admin);
        using var configVazia = await admin.GetAsync(PagamentoPixManualEndpoints.AdminPath);
        Assert.Equal(HttpStatusCode.NotFound, configVazia.StatusCode);
        foreach (var invalido in new[]
        {
            dto with { ChavePix = "invalida" }, dto with { TipoChave = "1" },
            dto with { NomeRecebedor = " " }, dto with { InstrucoesAdicionais = new string('x', 501) },
        })
        {
            using var recusado = await SalvarPixAsync(admin, invalido, token);
            Assert.Equal(HttpStatusCode.BadRequest, recusado.StatusCode);
        }
        Assert.Empty(await context.ConfiguracoesPixManual.AsNoTracking().ToListAsync());

        using var criado = await SalvarPixAsync(admin, dto, token);
        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        var antes = await cliente.GetFromJsonAsync<System.Text.Json.JsonElement>(url);
        Assert.Equal(dto.NomeRecebedor, antes.GetProperty("pix").GetProperty("nomeRecebedor").GetString());
        if (dto.TipoChave == "Cnpj")
        {
            using var criadoJson = await LerJsonPedidosAsync(criado);
            Assert.Equal("99999999999962", criadoJson.RootElement.GetProperty("chavePix").GetString());
        }
        var versaoAtual = dto with { ChavePix = novaChave, NomeRecebedor = "Loja de teste atualizada", InstrucoesAdicionais = "Confira o nome antes de pagar." };
        using var atualizado = await SalvarPixAsync(admin, versaoAtual, token);
        Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);
        Assert.True(atualizado.Headers.CacheControl?.NoStore);
        Assert.Equal(chaveNormalizada, (await context.ConfiguracoesPixManual.AsNoTracking().SingleAsync()).ChavePix);

        using var consultaAdmin = await admin.GetAsync(PagamentoPixManualEndpoints.AdminPath);
        using var consultaAdminJson = await LerJsonPedidosAsync(consultaAdmin);
        Assert.Equal(chaveNormalizada, consultaAdminJson.RootElement.GetProperty("chavePix").GetString());
        Assert.Equal(dto.TipoChave, consultaAdminJson.RootElement.GetProperty("tipoChave").GetString());
        using var disponivel = await cliente.GetAsync(url);
        using var json = await LerJsonPedidosAsync(disponivel);
        Assert.Equal(HttpStatusCode.OK, disponivel.StatusCode);
        Assert.True(disponivel.Headers.CacheControl?.NoStore);
        Assert.Equal("disponivel", json.RootElement.GetProperty("situacao").GetString());
        Assert.Equal(pedido.TotalCentavos.ToString(), json.RootElement.GetProperty("valorCentavos").GetString());
        Assert.Equal(pedido.PrazoPagamentoEmUtc, json.RootElement.GetProperty("prazoPagamentoEmUtc").GetDateTimeOffset());
        var pix = json.RootElement.GetProperty("pix");
        Assert.Equal(chaveNormalizada, pix.GetProperty("chavePix").GetString());
        Assert.Equal(dto.TipoChave, pix.GetProperty("tipoChave").GetString());
        Assert.Equal(versaoAtual.NomeRecebedor, pix.GetProperty("nomeRecebedor").GetString());
        Assert.Equal(versaoAtual.InstrucoesAdicionais, pix.GetProperty("instrucoesAdicionais").GetString());
        Assert.False(pix.TryGetProperty("id", out _));

        var novo = Pedido.Criar(pedido.ClienteId, agora, [item]);
        context.Pedidos.Add(novo);
        await context.SaveChangesAsync();
        var novoPix = await cliente.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/pedidos/{novo.Id:D}/pagamento");
        Assert.Equal(chaveNormalizada, novoPix.GetProperty("pix").GetProperty("chavePix").GetString());
        Assert.Equal(novo.TotalCentavos.ToString(), novoPix.GetProperty("valorCentavos").GetString());
        Assert.Equal(novo.PrazoPagamentoEmUtc, novoPix.GetProperty("prazoPagamentoEmUtc").GetDateTimeOffset());

        foreach (var fechado in new[] { pago, cancelado, expirado, vencido, entregue })
        {
            using var response = await cliente.GetAsync($"/api/pedidos/{fechado.Id:D}/pagamento");
            using var estado = await LerJsonPedidosAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pagamento_indisponivel", estado.RootElement.GetProperty("situacao").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, estado.RootElement.GetProperty("pix").ValueKind);
        }

        Assert.Equal(SituacaoPedido.AguardandoPagamento, await context.Pedidos.Where(p => p.Id == pedido.Id)
            .Select(p => p.Situacao).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, await context.ReservasProdutoAcabado.Select(r => r.Estado).SingleAsync());
        Assert.Equal(1, (await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    private static async Task<HttpResponseMessage> SalvarPixAsync(HttpClient client, object dados, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, PagamentoPixManualEndpoints.AdminPath)
        {
            Content = JsonContent.Create(dados, dados.GetType()),
        };
        if (token is not null) request.Headers.Add(AntiforgeryConfiguration.HeaderName, token);
        return await client.SendAsync(request);
    }
}
