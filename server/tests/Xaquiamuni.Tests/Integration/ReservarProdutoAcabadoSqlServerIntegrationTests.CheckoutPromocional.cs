using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task CheckoutPromocional_ValidaVigenciaRejeitaPrecoEConciliaSnapshot()
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var banco = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261121094657_CriarPromocoes", dbContextInterceptor: relogio);
        var db = banco.Services.GetRequiredService<XaquiamuniDbContext>();
        var (produto, padrao, alternativa) = await CriarProdutoCompravelAsync(db);
        await db.Database.MigrateAsync();
        await EntrarAsync(db, padrao.Id, 4);
        await EntrarAsync(db, alternativa.Id, 3);
        var clienteId = await CriarClienteCompraAsync(db);
        var adminId = await CriarAdministradorPagamentoAsync(db);
        var fim = Agora.AddMinutes(20);
        var geral = new { produtoId = produto.Id, tipo = "Percentual", escopo = "TodasVariantes",
            valor = "5000", inicioUtc = Agora, fimUtc = fim };
        var especifica = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "VariantesSelecionadas",
            valor = "900", inicioUtc = Agora, fimUtc = fim, variantesIds = new[] { padrao.Id } };
        var legado = new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = padrao.Id, quantidade = 1 } } };
        var chaveLegada = Guid.NewGuid();
        Guid especificaId;
        using (var desligado = CriarApiPedidos(banco, relogio))
        using (var admin = ClienteHttpPedidos(desligado, adminId, XaquiamuniRoles.Administrador))
        using (var cliente = ClienteHttpPedidos(desligado, clienteId))
        {
            var token = await CsrfPedidosAsync(admin);
            using var criadaGeral = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
                new { configuracao = geral, ativa = true }, Guid.NewGuid(), token);
            using var criadaEspecifica = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
                new { configuracao = especifica, ativa = true }, Guid.NewGuid(), token);
            Assert.Equal(HttpStatusCode.Created, criadaGeral.StatusCode);
            Assert.Equal(HttpStatusCode.Created, criadaEspecifica.StatusCode);
            especificaId = (await criadaEspecifica.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var antigo = await PostPedidoAsync(cliente, legado, chaveLegada, await CsrfPedidosAsync(cliente));
            Assert.Equal(HttpStatusCode.Created, antigo.StatusCode);
            Assert.Equal("1199", (await antigo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCentavos").GetString());
        }

        using var factory = CriarApiPedidos(banco, relogio, promocoesComerciais: true);
        using var novoCliente = ClienteHttpPedidos(factory, clienteId);
        using var novoAdmin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var csrf = await CsrfPedidosAsync(novoCliente);
        var csrfAdmin = await CsrfPedidosAsync(novoAdmin);
        var vitrine = $"/api/catalogo/produtos/{produto.Id:D}";
        relogio.Agora = Agora.AddTicks(-1);
        var antes = await novoCliente.GetFromJsonAsync<JsonElement>(vitrine);
        Assert.Equal("1199", PrecoFinal(antes, padrao.Id));
        relogio.Agora = Agora;
        var vigente = await novoCliente.GetFromJsonAsync<JsonElement>(vitrine);
        Assert.Equal("900", PrecoFinal(vigente, padrao.Id));
        Assert.Equal("501", PrecoFinal(vigente, alternativa.Id));
        var lista = await novoCliente.GetFromJsonAsync<JsonElement>("/api/catalogo/produtos");
        var resumo = Assert.Single(lista.GetProperty("itens").EnumerateArray());
        Assert.Equal(alternativa.Id, resumo.GetProperty("menorPreco").GetProperty("varianteId").GetGuid());
        Assert.Equal("501", resumo.GetProperty("menorPreco").GetProperty("preco").GetProperty("finalCentavos").GetString());

        using var replayLegado = await PostPedidoAsync(novoCliente, legado, chaveLegada, csrf);
        Assert.Equal(HttpStatusCode.OK, replayLegado.StatusCode);
        using var legadoNovo = await PostPedidoAsync(novoCliente, legado, Guid.NewGuid(), csrf);
        Assert.Equal(HttpStatusCode.Conflict, legadoNovo.StatusCode);
        var enviado = PedidoComPreco(produto.Id, padrao.Id, "900");
        using var adulterado = await PostPedidoAsync(novoCliente, PedidoComPreco(produto.Id, padrao.Id, "1"), Guid.NewGuid(), csrf);
        Assert.Equal(HttpStatusCode.Conflict, adulterado.StatusCode);
        Assert.Equal(1, await db.Pedidos.CountAsync());
        Assert.Equal(1, await db.ReservasProdutoAcabado.CountAsync());

        var chave = Guid.NewGuid();
        using var criada = await PostPedidoAsync(novoCliente, enviado, chave, csrf);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var pedido = await criada.Content.ReadFromJsonAsync<JsonElement>();
        var pedidoId = pedido.GetProperty("id").GetGuid();
        var item = Assert.Single(pedido.GetProperty("itens").EnumerateArray());
        Assert.Equal("900", item.GetProperty("subtotalCentavos").GetString());
        Assert.Equal("900", pedido.GetProperty("totalCentavos").GetString());
        Assert.Equal("1199", item.GetProperty("precoRegularCentavos").GetString());
        Assert.Equal(especificaId, item.GetProperty("promocaoId").GetGuid());

        relogio.Agora = fim;
        using var expirado = await PostPedidoAsync(novoCliente, enviado, Guid.NewGuid(), csrf);
        Assert.Equal(HttpStatusCode.Conflict, expirado.StatusCode);
        var erro = await expirado.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("preco_alterado", erro.GetProperty("codigo").GetString());
        Assert.Equal("1199", Assert.Single(erro.GetProperty("precosAtuais").EnumerateArray())
            .GetProperty("preco").GetProperty("finalCentavos").GetString());
        using var replay = await PostPedidoAsync(novoCliente, enviado, chave, csrf);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(pedido.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        using var divergente = await PostPedidoAsync(novoCliente, PedidoComPreco(produto.Id, padrao.Id, "1199"), chave, csrf);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.Equal("chave_em_uso", (await divergente.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
        Assert.Equal(2, await db.Pedidos.CountAsync());
        Assert.Equal(2, await db.ReservasProdutoAcabado.CountAsync());

        using var pixConfig = await SalvarPixAsync(novoAdmin,
            new SalvarPixManualRequest("Email", "loja@example.com", "Loja teste", null), csrfAdmin);
        Assert.True(pixConfig.IsSuccessStatusCode, await pixConfig.Content.ReadAsStringAsync());
        var pix = await novoCliente.GetFromJsonAsync<JsonElement>($"/api/pedidos/{pedidoId:D}/pagamento");
        Assert.Equal("disponivel", pix.GetProperty("situacao").GetString());
        Assert.Equal("900", pix.GetProperty("valorCentavos").GetString());
        using var confirmado = await EnviarComandoAdminPedidoAsync(novoAdmin,
            $"/api/admin/pedidos/{pedidoId:D}/confirmar-pagamento", new { recebimentoConferido = true }, csrfAdmin);
        Assert.Equal(HttpStatusCode.OK, confirmado.StatusCode);
        var movimentos = await novoAdmin.GetFromJsonAsync<JsonElement>("/api/admin/financeiro/movimentacoes");
        var movimento = Assert.Single(movimentos.GetProperty("itens").EnumerateArray());
        Assert.Equal("900", movimento.GetProperty("valorCentavos").GetString());
        Assert.Equal(pedidoId, movimento.GetProperty("recursoId").GetGuid());

        var variante = await db.ProdutoVariantes.SingleAsync(v => v.Id == padrao.Id);
        variante.AtualizarDados(variante.Identificacao, variante.UnidadeVenda, 12m);
        await db.SaveChangesAsync();
        var chaveNova = Guid.NewGuid();
        using var regularAlterado = await PostPedidoAsync(novoCliente, PedidoComPreco(produto.Id, padrao.Id, "1199"), chaveNova, csrf);
        Assert.Equal(HttpStatusCode.Conflict, regularAlterado.StatusCode);
        using var reconfirmado = await PostPedidoAsync(novoCliente, PedidoComPreco(produto.Id, padrao.Id, "1200"), Guid.NewGuid(), csrf);
        Assert.Equal(HttpStatusCode.Created, reconfirmado.StatusCode);
        var novoId = (await reconfirmado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var cancelado = await EnviarComandoAdminPedidoAsync(novoAdmin, $"/api/admin/pedidos/{novoId:D}/cancelar",
            new { motivo = "Teste" }, csrfAdmin);
        Assert.Equal(HttpStatusCode.OK, cancelado.StatusCode);
        Assert.Equal("Cancelado", (await cancelado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("situacao").GetString());
        Assert.Equal(3, await db.ReservasProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public Task CheckoutPromocional_AtivacaoConcorrenteProduzSnapshotOuRejeicaoSemReserva() =>
        VerificarMudancaConcorrenteAsync(edicao: false);

    [SqlServerIntegrationFact]
    public Task CheckoutPromocional_EdicaoConcorrenteProduzSnapshotOuRejeicaoSemReserva() =>
        VerificarMudancaConcorrenteAsync(edicao: true);

    private static async Task VerificarMudancaConcorrenteAsync(bool edicao)
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var banco = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        var db = banco.Services.GetRequiredService<XaquiamuniDbContext>();
        var (produto, padrao, _) = await CriarProdutoCompravelAsync(db);
        await EntrarAsync(db, padrao.Id, 2);
        var clienteId = await CriarClienteCompraAsync(db);
        using var factory = CriarApiPedidos(banco, relogio, promocoesComerciais: true);
        using var cliente = ClienteHttpPedidos(factory, clienteId);
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        var csrf = await CsrfPedidosAsync(cliente);
        var token = await CsrfPedidosAsync(admin);
        var config = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "TodasVariantes",
            valor = edicao ? "1000" : "800" };
        using var preparada = await EnviarPromocaoAsync(admin, HttpMethod.Post, PromocoesUrl,
            new { configuracao = config, ativa = edicao }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Created, preparada.StatusCode);
        var id = (await preparada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var precoAceito = edicao ? "1000" : "1199";
        var criacao = PostPedidoAsync(cliente, PedidoComPreco(produto.Id, padrao.Id, precoAceito), Guid.NewGuid(), csrf);
        var alteracao = edicao
            ? EnviarPromocaoAsync(admin, HttpMethod.Put, $"{PromocoesUrl}/{id:D}",
                new { configuracao = config with { valor = "800" }, versao = 1 }, Guid.NewGuid(), token)
            : EnviarPromocaoAsync(admin, HttpMethod.Patch, $"{PromocoesUrl}/{id:D}/status",
                new { ativa = true, versao = 1 }, Guid.NewGuid(), token);
        await Task.WhenAll(criacao, alteracao);
        using var compra = await criacao;
        using var alterada = await alteracao;
        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);
        if (compra.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(edicao ? 1000L : 1199L, (await db.Pedidos.SingleAsync()).TotalCentavos);
            Assert.Equal(1, await db.ReservasProdutoAcabado.CountAsync());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, compra.StatusCode);
            Assert.Equal("preco_alterado", (await compra.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
            Assert.Equal(0, await db.Pedidos.CountAsync());
            Assert.Equal(0, await db.ReservasProdutoAcabado.CountAsync());
        }
        using var precoNovo = await PostPedidoAsync(cliente, PedidoComPreco(produto.Id, padrao.Id, "800"), Guid.NewGuid(), csrf);
        Assert.Equal(HttpStatusCode.Created, precoNovo.StatusCode);
    }

    private static object PedidoComPreco(Guid produtoId, Guid varianteId, string preco) =>
        new { itens = new[] { new { produtoId, produtoVarianteId = varianteId, quantidade = 1, precoAceitoCentavos = preco } } };

    private static string? PrecoFinal(JsonElement produto, Guid varianteId) =>
        Assert.Single(produto.GetProperty("opcoes").EnumerateArray(), v => v.GetProperty("id").GetGuid() == varianteId)
            .GetProperty("preco").GetProperty("finalCentavos").GetString();
}
