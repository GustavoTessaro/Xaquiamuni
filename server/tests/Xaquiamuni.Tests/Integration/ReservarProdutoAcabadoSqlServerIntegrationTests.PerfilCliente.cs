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
