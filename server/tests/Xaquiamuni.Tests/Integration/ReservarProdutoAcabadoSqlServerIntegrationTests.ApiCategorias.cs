using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ApiCategoriaSql_ExigeAutorizacaoCsrfVersaoERetornaReplay()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var categoria = Categoria.Criar("Exclusão pela API", null, 0);
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync();
        using var api = CriarApiPedidos(banco);
        using var anonimo = ClienteHttpPedidos(api);
        using var cliente = ClienteHttpPedidos(api, await CriarClienteCompraAsync(db));
        using var admin = ClienteHttpPedidos(api, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        var rota = $"/api/admin/categorias/{categoria.Id:D}";
        using var detalhe = await admin.GetAsync(rota);
        detalhe.EnsureSuccessStatusCode();
        var versao = detalhe.Headers.ETag!.Tag;
        var token = await CsrfPedidosAsync(admin);
        var chave = Guid.NewGuid();
        await Verificar(anonimo, HttpStatusCode.Unauthorized, versao, token);
        await Verificar(cliente, HttpStatusCode.Forbidden, versao, token);
        await Verificar(admin, HttpStatusCode.BadRequest, versao, null);
        await Verificar(admin, HttpStatusCode.BadRequest, versao, "invalido");
        await Verificar(admin, HttpStatusCode.BadRequest, null, token);
        await Verificar(admin, HttpStatusCode.BadRequest, "*", token);
        await Verificar(admin, HttpStatusCode.Conflict, $"\"{Guid.NewGuid():D}\"", token, "versao_desatualizada");
        await Verificar(admin, HttpStatusCode.NoContent, versao, token);
        await Verificar(admin, HttpStatusCode.BadRequest, versao, null);
        await Verificar(admin, HttpStatusCode.NoContent, versao, token);
        await Verificar(admin, HttpStatusCode.Conflict, $"\"{Guid.NewGuid():D}\"", token, "chave_em_uso");

        async Task Verificar(HttpClient http, HttpStatusCode esperado, string? tag, string? csrf, string? codigo = null)
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Delete, rota);
            requisicao.Headers.Add("Idempotency-Key", chave.ToString("D"));
            if (tag is not null) requisicao.Headers.TryAddWithoutValidation("If-Match", tag);
            if (csrf is not null) requisicao.Headers.Add("X-XSRF-TOKEN", csrf);
            using var resposta = await http.SendAsync(requisicao);
            Assert.Equal(esperado, resposta.StatusCode);
            if (codigo is not null)
            {
                var problema = await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                Assert.Equal(codigo, problema.GetProperty("codigo").GetString());
            }
        }
    }

    [SqlServerIntegrationFact]
    public async Task ApiCategoriaSql_BloqueiaProdutoPublicadoSemEstoque()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (produto, _, _) = await CriarProdutoCompravelAsync(db);
        var categoria = await db.Categorias.SingleAsync(x => x.Id == produto.CategoriaId);
        using var api = CriarApiPedidos(banco);
        using var admin = ClienteHttpPedidos(api, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/categorias/{categoria.Id:D}");
        request.Headers.IfMatch.ParseAdd($"\"{categoria.Versao:D}\"");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Headers.Add("X-XSRF-TOKEN", await CsrfPedidosAsync(admin));
        using var resposta = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var problema = await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("categoria_com_produtos", problema.GetProperty("codigo").GetString());
    }
}

