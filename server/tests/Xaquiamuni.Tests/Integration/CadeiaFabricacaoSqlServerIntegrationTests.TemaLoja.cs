using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string TemaPublico = "/api/loja/tema";
    private const string TemaAdmin = "/api/admin/loja/tema";

    [SqlServerIntegrationFact]
    public async Task TemaLoja_PublicacaoConcorrenteReplayEReinicioPreservamUmaUnicaConfiguracao()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        var diretorio = Directory.CreateTempSubdirectory("xaquiamuni-temas-e2e-");
        try
        {
            await using (var host = new HostE2ECompra(banco, diretorio.FullName, diretorio.FullName))
            {
                using var admin = await Sessao.EntrarAsync(host, XaquiamuniRoles.Administrador);
                using var outra = await Sessao.EntrarAsync(host, XaquiamuniRoles.Administrador);
                using var cliente = await Sessao.EntrarAsync(host, XaquiamuniRoles.Cliente);
                var inicial = await cliente.Http.GetFromJsonAsync<JsonElement>(TemaPublico);
                Assert.Equal("original", inicial.GetProperty("chave").GetString());
                Assert.Equal(0, inicial.GetProperty("revisao").GetInt32());
                Assert.Equal(2, inicial.EnumerateObject().Count());

                var chave = Guid.NewGuid();
                using var proibida = await PublicarTemaAsync(cliente.Http, "original", 0, chave);
                Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
                using var semCsrf = await PublicarTemaAsync(admin.Http, "original", 0, chave, comCsrf: false);
                Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
                using var sazonal = await PublicarTemaAsync(admin.Http, "natal", 0, chave);
                Assert.Equal(HttpStatusCode.BadRequest, sazonal.StatusCode);
                using var desconhecida = await PublicarTemaAsync(admin.Http, "tema-inexistente", 0, chave);
                Assert.Equal(HttpStatusCode.BadRequest, desconhecida.StatusCode);
                var dados = await admin.Http.GetFromJsonAsync<JsonElement>(TemaAdmin);
                Assert.Equal(6, dados.GetProperty("temas").GetArrayLength());
                Assert.Single(dados.GetProperty("temas").EnumerateArray(), tema => tema.GetProperty("disponivel").GetBoolean());

                var respostas = await Task.WhenAll(
                    PublicarTemaAsync(admin.Http, "original", 0, chave),
                    PublicarTemaAsync(outra.Http, "original", 0, chave));
                using var primeira = respostas[0];
                using var segunda = respostas[1];
                Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
                Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
                var venceu = primeira.IsSuccessStatusCode ? admin : outra;
                var resultado = await respostas.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(1, resultado.GetProperty("revisao").GetInt32());
                Assert.NotEqual(Guid.Empty, resultado.GetProperty("administradorId").GetGuid());
                Assert.Equal(TimeSpan.Zero, resultado.GetProperty("publicadoEmUtc").GetDateTimeOffset().Offset);
                using var replay = await PublicarTemaAsync(venceu.Http, "original", 0, chave);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Equal(resultado.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
                using var chaveReutilizada = await PublicarTemaAsync(venceu.Http, "original", 1, chave);
                Assert.Equal(HttpStatusCode.Conflict, chaveReutilizada.StatusCode);

                await using var db = banco.CreateVerificationContext();
                var registro = Assert.Single(await db.ConfiguracoesTemaLoja.AsNoTracking().ToListAsync());
                Assert.Equal(1, registro.Revisao);
            }

            await using var reiniciado = new HostE2ECompra(banco, diretorio.FullName, diretorio.FullName);
            using var sessao = await Sessao.EntrarAsync(reiniciado, XaquiamuniRoles.Cliente);
            using var resposta = await sessao.Http.GetAsync(TemaPublico);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.True(resposta.Headers.CacheControl?.NoCache);
            var lida = await resposta.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, lida.GetProperty("revisao").GetInt32());
            using var condicional = new HttpRequestMessage(HttpMethod.Get, TemaPublico);
            condicional.Headers.IfNoneMatch.Add(resposta.Headers.ETag!);
            using var revalidada = await sessao.Http.SendAsync(condicional);
            Assert.Equal(HttpStatusCode.NotModified, revalidada.StatusCode);
        }
        finally
        {
            diretorio.Delete(recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> PublicarTemaAsync(HttpClient http, string chave,
        int revisaoEsperada, Guid idempotencia, bool comCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, TemaAdmin)
        {
            Content = JsonContent.Create(new { chave, revisaoEsperada }),
        };
        request.Headers.Add("Idempotency-Key", idempotencia.ToString("D"));
        if (comCsrf)
        {
            var csrf = await http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        }
        return await http.SendAsync(request);
    }
}
