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
    public async Task PerfilCliente_AtualizaSomenteCamposPermitidosComCsrfEVersoesIndependentes()
    {
        const string perfil = "/api/clientes/me/perfil";
        const string endereco = "/api/clientes/me/endereco-padrao";
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var db = database.CreateVerificationContext();
        var id = await CriarClienteCompraAsync(db);
        var outroId = await CriarClienteCompraAsync(db);
        var usuario = await db.Users.SingleAsync(x => x.Id == id);
        usuario.Email = "cliente@example.test";
        usuario.PasswordHash = "fixture";
        await db.SaveChangesAsync();
        var cpf = (await db.ClientePerfis.AsNoTracking().SingleAsync(x => x.UserId == id)).CpfProtegido;

        using var factory = CriarApiPedidos(database);
        using var cliente = ClienteHttpPedidos(factory, id);
        using var outraAba = ClienteHttpPedidos(factory, id);
        using var outro = ClienteHttpPedidos(factory, outroId);
        using var admin = ClienteHttpPedidos(factory, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        using var anonimo = ClienteHttpPedidos(factory);
        using var leitura = await cliente.GetAsync(perfil);
        var vp = Assert.Single(leitura.Headers.GetValues("X-Perfil-Versao"));
        var ve = Assert.Single(leitura.Headers.GetValues("X-Endereco-Versao"));
        var dados = new { nomeCompleto = "  Cliente  Atualizado ", telefone = " (54) 99900-0000 " };
        var destino = new { cep = "90000-000", uf = "rs", cidade = "Porto Alegre", bairro = "Centro",
            logradouro = "Rua de teste", numero = "14", complemento = " Apto 1 " };

        async Task<HttpStatusCode> Enviar(HttpClient http, string rota, object corpo, string versao, string? csrf)
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, rota) { Content = JsonContent.Create(corpo) };
            req.Headers.TryAddWithoutValidation("If-Match", versao);
            if (csrf is not null) req.Headers.Add("X-XSRF-TOKEN", csrf);
            using var resp = await http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                Assert.True(resp.Headers.CacheControl?.Private);
                Assert.True(resp.Headers.CacheControl?.NoStore);
            }
            return resp.StatusCode;
        }

        var token = await CsrfPedidosAsync(cliente);
        var tokenAba = await CsrfPedidosAsync(outraAba);
        Assert.Equal(HttpStatusCode.Unauthorized, await Enviar(anonimo, perfil, dados, vp, null));
        Assert.Equal(HttpStatusCode.Forbidden, await Enviar(admin, perfil, dados, vp, null));
        Assert.Equal(HttpStatusCode.BadRequest, await Enviar(cliente, perfil, dados, vp, null));
        Assert.Equal(HttpStatusCode.BadRequest, await Enviar(cliente, perfil, dados, vp, "invalido"));
        Assert.Equal(HttpStatusCode.BadRequest, await Enviar(cliente, perfil, new
        {
            dados.nomeCompleto, dados.telefone, email = "outro@example.test", cpf = "indevido",
        }, vp, token));
        Assert.Equal(HttpStatusCode.BadRequest, await Enviar(cliente, perfil, new { nomeCompleto = "", telefone = "123" }, vp, token));
        Assert.Equal(HttpStatusCode.BadRequest, await Enviar(cliente, endereco, destino with { uf = "XX" }, ve, token));
        Assert.Equal(HttpStatusCode.Conflict, await Enviar(outro, $"{perfil}?clienteId={id:D}", dados, vp, await CsrfPedidosAsync(outro)));

        var paralelas = await Task.WhenAll(
            Enviar(cliente, perfil, dados, vp, token),
            Enviar(outraAba, endereco, destino, ve, tokenAba));
        Assert.All(paralelas, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.Conflict, await Enviar(outraAba, perfil, dados, vp, tokenAba));
        Assert.Equal(HttpStatusCode.Conflict, await Enviar(cliente, endereco, destino, ve, token));

        using var atual = await cliente.GetAsync(perfil);
        var veAtual = Assert.Single(atual.Headers.GetValues("X-Endereco-Versao"));
        var resumo = await atual.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cliente Atualizado", resumo.GetProperty("nomeCompleto").GetString());
        Assert.Equal("(54) 99900-0000", resumo.GetProperty("telefone").GetString());
        Assert.Equal("cliente@example.test", resumo.GetProperty("email").GetString());
        Assert.Equal("90000000", resumo.GetProperty("endereco").GetProperty("cep").GetString());

        var disputa = await Task.WhenAll(
            Enviar(cliente, endereco, destino with { numero = "21" }, veAtual, token),
            Enviar(outraAba, endereco, destino with { numero = "22" }, veAtual, tokenAba));
        Assert.Contains(HttpStatusCode.OK, disputa);
        Assert.Contains(HttpStatusCode.Conflict, disputa);
        Assert.Single(await db.ClienteEnderecos.AsNoTracking().Where(x => x.ClienteUserId == id && x.IsDefault).ToListAsync());
        var salvo = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal("cliente@example.test", salvo.Email);
        Assert.Equal("fixture", salvo.PasswordHash);
        var cpfAtual = (await db.ClientePerfis.AsNoTracking().SingleAsync(x => x.UserId == id)).CpfProtegido;
        Assert.Equal(cpf.Ciphertext, cpfAtual.Ciphertext);
        Assert.Equal(cpf.Fingerprint, cpfAtual.Fingerprint);
    }

    [SqlServerIntegrationFact]
    public async Task PerfilCliente_ProjetaSomenteTitularESuportaCadastroIncompleto()
    {
        const string rota = "/api/clientes/me/perfil";
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var titular = await CriarClienteCompraAsync(context);
        var outro = await CriarClienteCompraAsync(context);
        var semPerfil = Guid.NewGuid();
        context.Users.Add(new XaquiamuniUser { Id = semPerfil, Email = "incompleto@example.test" });
        var conta = await context.Users.SingleAsync(x => x.Id == titular);
        conta.Email = "titular@example.test";
        conta.PhoneNumber = "+5554990000000";
        conta.PasswordHash = "hash-somente-para-fixture";
        context.ClienteEnderecos.AddRange(
            ClienteEndereco.Criar(Guid.NewGuid(), titular, "95200-000", "RS", "Vacaria", "Centro", "Rua do titular", "12", null, true),
            ClienteEndereco.Criar(Guid.NewGuid(), titular, "95200-000", "RS", "Vacaria", "Centro", "Rua alternativa", "34", null, false));
        await context.SaveChangesAsync();

        using var factory = CriarApiPedidos(database);
        using var anonimo = ClienteHttpPedidos(factory);
        using var admin = ClienteHttpPedidos(factory, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        using var cliente = ClienteHttpPedidos(factory, titular);
        using var outroCliente = ClienteHttpPedidos(factory, outro);
        using var incompleto = ClienteHttpPedidos(factory, semPerfil);
        using var respostaAnonima = await anonimo.GetAsync(rota);
        using var respostaAdmin = await admin.GetAsync(rota);
        Assert.Equal(HttpStatusCode.Unauthorized, respostaAnonima.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, respostaAdmin.StatusCode);

        using var resposta = await cliente.GetAsync($"{rota}?clienteId={outro:D}&userId={outro:D}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.Private);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        var perfil = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "email", "endereco", "nomeCompleto", "telefone" },
            perfil.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Cliente de teste", perfil.GetProperty("nomeCompleto").GetString());
        Assert.Equal("titular@example.test", perfil.GetProperty("email").GetString());
        Assert.Equal("+5554990000000", perfil.GetProperty("telefone").GetString());
        var endereco = perfil.GetProperty("endereco");
        Assert.Equal(new[] { "bairro", "cep", "cidade", "complemento", "logradouro", "numero", "uf" },
            endereco.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Rua do titular", endereco.GetProperty("logradouro").GetString());

        var semEndereco = await outroCliente.GetFromJsonAsync<JsonElement>($"{rota}?clienteId={titular:D}");
        Assert.Equal(JsonValueKind.Null, semEndereco.GetProperty("endereco").ValueKind);
        Assert.Equal(JsonValueKind.Null, semEndereco.GetProperty("email").ValueKind);

        var dadosIncompletos = await incompleto.GetFromJsonAsync<JsonElement>(rota);
        Assert.Equal(JsonValueKind.Null, dadosIncompletos.GetProperty("nomeCompleto").ValueKind);
        Assert.Equal("incompleto@example.test", dadosIncompletos.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, dadosIncompletos.GetProperty("endereco").ValueKind);
    }
}
