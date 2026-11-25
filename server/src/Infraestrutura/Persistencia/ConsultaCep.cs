using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Xaquiamuni.Aplicacao.Pedidos;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed class ConsultaCepMapas(
    IReadOnlyDictionary<string, string> entrega,
    IReadOnlyDictionary<string, MunicipioPostal> postal,
    IReadOnlyCollection<string>? indisponiveis = null) : IConsultaCep
{
    public Task<ResultadoCep> ConsultarAsync(string? cep, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizado = ResultadoCep.Normalizar(cep);
        return Task.FromResult<ResultadoCep>(normalizado is null ? new(null, "cep_invalido") :
            indisponiveis?.Contains(normalizado) == true ? new(null, "cep_indisponivel") :
            postal.TryGetValue(normalizado, out var municipio) ? new(new(normalizado, municipio.Uf, municipio.Cidade,
                municipio.CodigoIbge, municipio.Bairro, municipio.Logradouro)) :
            entrega.TryGetValue(normalizado, out var ibge) ? new(new(normalizado, null, null, ibge)) :
            new(null, "cep_nao_encontrado"));
    }
}

internal sealed class ConsultaViaCep(IHttpClientFactory clientes) : IConsultaCep, IDisposable
{
    internal const string Cliente = "ViaCEP";
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 2048 });
    // Faixas fixas limitam a memória e evitam duplicar consultas em cache miss.
    private readonly SemaphoreSlim[] _faixas = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public async Task<ResultadoCep> ConsultarAsync(string? cep, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizado = ResultadoCep.Normalizar(cep);
        if (normalizado is null) return new(null, "cep_invalido");
        if (_cache.TryGetValue<ResultadoCep>(normalizado, out var salvo)) return salvo!;
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(3));
        var faixa = _faixas[int.Parse(normalizado, System.Globalization.CultureInfo.InvariantCulture) % _faixas.Length];
        try
        {
            await faixa.WaitAsync(limite.Token);
            try
            {
                if (_cache.TryGetValue<ResultadoCep>(normalizado, out salvo)) return salvo!;
                var resultado = await BuscarAsync(normalizado, limite.Token);
                if (resultado.Endereco is not null || resultado.Codigo == "cep_nao_encontrado")
                    _cache.Set(normalizado, resultado, new MemoryCacheEntryOptions().SetSize(1)
                        .SetAbsoluteExpiration(resultado.Endereco is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(6)));
                return resultado;
            }
            finally { faixa.Release(); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(null, "cep_indisponivel");
        }
        catch (Exception erro) when (erro is HttpRequestException or JsonException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return new(null, "cep_indisponivel");
        }
    }

    private async Task<ResultadoCep> BuscarAsync(string cep, CancellationToken ct)
    {
        using var cliente = clientes.CreateClient(Cliente);
        using var resposta = await cliente.GetAsync($"ws/{cep}/json/", ct);
        if (resposta.StatusCode != HttpStatusCode.OK) return new(null, "cep_indisponivel");
        var json = await resposta.Content.ReadAsStringAsync(ct);
        using var documento = JsonDocument.Parse(json);
        var objeto = documento.RootElement;
        if (objeto.ValueKind != JsonValueKind.Object) return new(null, "cep_indisponivel");
        if (objeto.TryGetProperty("erro", out var erro) &&
            (erro.ValueKind == JsonValueKind.True || (erro.ValueKind == JsonValueKind.String && erro.GetString() == "true")))
            return new(null, "cep_nao_encontrado");

        string? Campo(string nome, int max) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String
            && valor.GetString()?.Trim() is { Length: > 0 } texto && texto.Length <= max ? texto : null;
        var uf = Campo("uf", 2);
        var cidade = Campo("localidade", 100);
        var ibge = Campo("ibge", 7);
        if (ResultadoCep.Normalizar(Campo("cep", 9)) != cep ||
            uf is not { Length: 2 } || !UfIbge.TryGetValue(uf, out var prefixo) || cidade is null ||
            ibge is not { Length: 7 } || !ibge.All(char.IsAsciiDigit) || !ibge.StartsWith(prefixo, StringComparison.Ordinal))
            return new(null, "cep_indisponivel");
        return new(new(cep, uf, cidade, ibge, Campo("bairro", 100), Campo("logradouro", 150)));
    }

    private static readonly Dictionary<string, string> UfIbge = new()
    {
        ["RO"]="11", ["AC"]="12", ["AM"]="13", ["RR"]="14", ["PA"]="15", ["AP"]="16", ["TO"]="17",
        ["MA"]="21", ["PI"]="22", ["CE"]="23", ["RN"]="24", ["PB"]="25", ["PE"]="26", ["AL"]="27",
        ["SE"]="28", ["BA"]="29", ["MG"]="31", ["ES"]="32", ["RJ"]="33", ["SP"]="35", ["PR"]="41",
        ["SC"]="42", ["RS"]="43", ["MS"]="50", ["MT"]="51", ["GO"]="52", ["DF"]="53"
    };

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var faixa in _faixas) faixa.Dispose();
    }
}
