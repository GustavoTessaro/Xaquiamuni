using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task PromocoesFrete_AdminMigraProtegeEditaEConcorreSemAfetarPedido()
    {
        const string rota = "/api/admin/promocoes-frete";
        await using var banco = await SqlServerTestDatabase.CreateAsync(targetMigration: "20261123200209_AdicionarNumeroPublicoPedido");
        await using var db = banco.CreateVerificationContext();
        var clienteId = await CriarClienteCompraAsync(db);
        var (produto, variante, _) = await CriarProdutoCompravelAsync(db);
        await EntrarAsync(db, variante.Id, 2);
        var pedido = (await Compra(db).ExecuteAsync(new(clienteId, Guid.NewGuid(), [new(produto.Id, variante.Id, 1)]))).Pedido!;
        await db.Database.MigrateAsync();
        using var factory = CriarApiPedidos(banco);
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        using var aba = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        using var cliente = ClienteHttpPedidos(factory, clienteId);
        using var anonimo = ClienteHttpPedidos(factory);
        var token = await CsrfPedidosAsync(admin);
        var tokenAba = await CsrfPedidosAsync(aba);
        var dados = new { nome = "  Frete da loja ", tipo = "Percentual", valor = "1250",
            modalidades = new[] { "EntregaPropria", "CorreiosManual" }, subtotalMinimoCentavos = "0",
            inicioUtc = "2026-11-23T08:00:00-03:00", fimUtc = "2026-11-25T08:00:00-03:00" };

        static async Task<HttpResponseMessage> Enviar(HttpClient http, HttpMethod metodo, string uri,
            object corpo, string? csrf, string? etag = null)
        {
            using var req = new HttpRequestMessage(metodo, uri) { Content = JsonContent.Create(corpo) };
            if (csrf is not null) req.Headers.Add("X-XSRF-TOKEN", csrf);
            if (etag is not null) req.Headers.TryAddWithoutValidation("If-Match", etag);
            return await http.SendAsync(req);
        }

        using var semSessao = await anonimo.GetAsync(rota);
        using var acessoCliente = await cliente.GetAsync(rota);
        using var semCsrf = await Enviar(admin, HttpMethod.Post, rota, dados, null);
        using var invalida = await Enviar(admin, HttpMethod.Post, rota, dados with { valor = "10001" }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, acessoCliente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        using var criada = await Enviar(admin, HttpMethod.Post, rota, dados, token);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        Assert.True(criada.Headers.CacheControl?.Private);
        Assert.True(criada.Headers.CacheControl?.NoStore);
        Assert.Equal("\"1\"", criada.Headers.ETag?.Tag);
        var corpo = await criada.Content.ReadFromJsonAsync<JsonElement>();
        var id = corpo.GetProperty("id").GetGuid();
        var detalhe = rota + "/" + id;
        Assert.False(corpo.GetProperty("ativa").GetBoolean());
        Assert.Equal("Frete da loja", corpo.GetProperty("nome").GetString());
        Assert.Equal(TimeSpan.Zero, corpo.GetProperty("inicioUtc").GetDateTimeOffset().Offset);
        Assert.Equal(Responsavel, corpo.GetProperty("criadaPorId").GetGuid());
        using var lida = await admin.GetAsync(detalhe);
        using var faltaVersao = await Enviar(admin, HttpMethod.Put, detalhe, dados, token);
        Assert.Equal(criada.Headers.ETag?.Tag, lida.Headers.ETag?.Tag);
        Assert.Equal((HttpStatusCode)428, faltaVersao.StatusCode);

        var escritas = await Task.WhenAll(
            Enviar(admin, HttpMethod.Put, detalhe, dados with { nome = "Alternativa A" }, token, "\"1\""),
            Enviar(aba, HttpMethod.Put, detalhe, dados with { nome = "Alternativa B" }, tokenAba, "\"1\""));
        using var a = escritas[0];
        using var b = escritas[1];
        Assert.Single(escritas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(escritas, r => r.StatusCode == HttpStatusCode.Conflict);
        using var ativa = await Enviar(admin, HttpMethod.Patch, detalhe + "/status", new { ativa = true }, token, "\"2\"");
        Assert.Equal(HttpStatusCode.OK, ativa.StatusCode);
        var lista = await admin.GetFromJsonAsync<JsonElement>(rota + "?pagina=1");
        Assert.True(Assert.Single(lista.GetProperty("itens").EnumerateArray()).GetProperty("ativa").GetBoolean());
        using var inativa = await Enviar(admin, HttpMethod.Patch, detalhe + "/status", new { ativa = false }, token, "\"3\"");
        Assert.Equal(HttpStatusCode.OK, inativa.StatusCode);
        var persistida = await db.Set<PromocaoFrete>().SingleAsync();
        Assert.False(persistida.Ativa);
        Assert.Equal(4, persistida.Versao);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [PromocoesFrete] SET [Modalidades] = 4 WHERE [Id] = {id}"));
        var preservado = await db.Pedidos.AsNoTracking().SingleAsync();
        Assert.Equal(pedido.NumeroPublico, preservado.NumeroPublico);
        Assert.Equal(pedido.TotalCentavos, preservado.TotalCentavos);
        Assert.Equal(pedido.FreteCentavos, preservado.FreteCentavos);
        Assert.Equal(pedido.Situacao, preservado.Situacao);
        Assert.Equal(1, await db.ReservasProdutoAcabado.CountAsync());
    }
}
