using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public sealed class ConsultaCepTests
{
    [Theory]
    [InlineData("01001-000", "01001000")]
    [InlineData(" 01001000 ", "01001000")]
    [InlineData("0100-1000", null)]
    [InlineData("01001 000", null)]
    [InlineData("０1001000", null)]
    public void NormalizaSomenteOitoDigitosAscii(string valor, string? esperado) =>
        Assert.Equal(esperado, ResultadoCep.Normalizar(valor));

    [Fact]
    public async Task MantemZerosAEsquerdaSuportaCepGeralEReutilizaCache()
    {
        using var transporte = new Transporte("""
            {"cep":"01001-000","localidade":"São Paulo","uf":"SP","ibge":"3550308","bairro":"","logradouro":""}
            """);
        using var consulta = new ConsultaViaCep(transporte);
        Assert.Equal("cep_invalido", (await consulta.ConsultarAsync("../teste", default)).Codigo);
        Assert.Equal(0, transporte.Chamadas);
        var resultado = await consulta.ConsultarAsync("01001-000", default);
        Assert.Equal(new EnderecoCep("01001000", "SP", "São Paulo", "3550308"), resultado.Endereco);
        Assert.Equal(resultado, await consulta.ConsultarAsync("01001000", default));
        Assert.Equal(1, transporte.Chamadas);
        Assert.Equal("/ws/01001000/json/", transporte.Caminho);
    }

    [Theory]
    [InlineData(200, """{"erro":true}""", "cep_nao_encontrado", 1)]
    [InlineData(200, """{"erro":"true"}""", "cep_nao_encontrado", 1)]
    [InlineData(200, """{"cep":"01001-000","localidade":"São Paulo","uf":"SP","ibge":"4209300"}""", "cep_indisponivel", 2)]
    [InlineData(200, """{"cep":"01001-001","localidade":"São Paulo","uf":"SP","ibge":"3550308"}""", "cep_indisponivel", 2)]
    [InlineData(200, "não é JSON", "cep_indisponivel", 2)]
    [InlineData(429, "", "cep_indisponivel", 2)]
    [InlineData(503, "", "cep_indisponivel", 2)]
    public async Task DistingueAusenciaDeFalhasTransitorias(int status, string json, string codigo, int chamadas)
    {
        using var transporte = new Transporte(json, (HttpStatusCode)status);
        using var consulta = new ConsultaViaCep(transporte);
        Assert.Equal(codigo, (await consulta.ConsultarAsync("01001000", default)).Codigo);
        Assert.Equal(codigo, (await consulta.ConsultarAsync("01001000", default)).Codigo);
        Assert.Equal(chamadas, transporte.Chamadas);
    }

    [Fact]
    public async Task PropagaCancelamentoDoCliente()
    {
        using var transporte = new Transporte("{}");
        using var consulta = new ConsultaViaCep(transporte);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consulta.ConsultarAsync("01001000", new(true)));
        Assert.Equal(0, transporte.Chamadas);
    }

    [Fact]
    public async Task EndpointPublicoUsaMapasEmTestingEAplicaLimiteDeFrequencia()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "",
                ["ConsultaCep:Provedor"] = "ViaCEP",
                ["CorreiosManual:MunicipiosPorCep:95200000:Cidade"] = "Vacaria",
                ["CorreiosManual:MunicipiosPorCep:95200000:Uf"] = "RS",
                ["CorreiosManual:MunicipiosPorCep:95200000:CodigoIbge"] = "4322509",
                ["ConsultaCep:Indisponiveis:0"] = "99999998"
            }));
        });
        using var cliente = factory.CreateClient();
        using var ok = await cliente.GetAsync("/api/cep/95200-000");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ok.Headers.CacheControl?.NoStore);
        Assert.Equal("4322509", (await ok.Content.ReadFromJsonAsync<EnderecoCep>())?.CodigoIbge);
        using var invalido = await cliente.GetAsync("/api/cep/123");
        using var ausente = await cliente.GetAsync("/api/cep/99999999");
        using var indisponivel = await cliente.GetAsync("/api/cep/99999998");
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, indisponivel.StatusCode);
        for (var i = 0; i < 6; i++)
        {
            using var dentro = await cliente.GetAsync("/api/cep/95200000");
            Assert.Equal(HttpStatusCode.OK, dentro.StatusCode);
        }
        using var limite = await cliente.GetAsync("/api/cep/95200000");
        Assert.Equal(HttpStatusCode.TooManyRequests, limite.StatusCode);
        Assert.NotNull(limite.Headers.RetryAfter);
    }

    private sealed class Transporte(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler, IHttpClientFactory
    {
        public int Chamadas { get; private set; }
        public string? Caminho { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new("https://viacep.com.br/") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            Caminho = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
