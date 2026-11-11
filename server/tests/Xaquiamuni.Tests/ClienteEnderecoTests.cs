using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public class ClienteEnderecoTests
{
    [Fact]
    public void Criar_ComDadosValidosNormalizaCamposERepresentaCepSemMascara()
    {
        var id = Guid.NewGuid();
        var clienteUserId = Guid.NewGuid();

        var address = CreateAddress(
            id,
            clienteUserId,
            cep: " 95000-000 ",
            uf: " rs ",
            cidade: "  Vacaria ",
            bairro: " Centro\t Histórico ",
            logradouro: "  Rua   das Flores ",
            numero: " 12 ",
            complemento: "  Casa   2 ",
            isDefault: true);

        Assert.Equal(id, address.Id);
        Assert.Equal(clienteUserId, address.ClienteUserId);
        Assert.Equal("95000000", address.Cep);
        Assert.Equal("RS", address.Uf);
        Assert.Equal("Vacaria", address.Cidade);
        Assert.Equal("Centro Histórico", address.Bairro);
        Assert.Equal("Rua das Flores", address.Logradouro);
        Assert.Equal("12", address.Numero);
        Assert.Equal("Casa 2", address.Complemento);
        Assert.True(address.IsDefault);
    }

    [Fact]
    public void Criar_AceitaCepSemHifen()
    {
        var address = CreateAddress(cep: "95000000");

        Assert.Equal("95000000", address.Cep);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Criar_RejeitaIdentificadoresVazios(bool invalidId, bool invalidClientId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CreateAddress(
                id: invalidId ? Guid.Empty : Guid.NewGuid(),
                clienteUserId: invalidClientId ? Guid.Empty : Guid.NewGuid()));

        Assert.Equal(invalidId ? "id" : "clienteUserId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9500000")]
    [InlineData("950000000")]
    [InlineData("95A00-000")]
    [InlineData("95000 000")]
    [InlineData("９５００００００")]
    public void Criar_RejeitaCepInvalido(string? cep)
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateAddress(cep: cep));

        Assert.Equal("cep", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("R")]
    [InlineData("ZZ")]
    [InlineData("R S")]
    public void Criar_RejeitaUfInvalida(string? uf)
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateAddress(uf: uf));

        Assert.Equal("uf", exception.ParamName);
    }

    [Theory]
    [InlineData("cidade")]
    [InlineData("bairro")]
    [InlineData("logradouro")]
    [InlineData("numero")]
    public void Criar_RejeitaCamposObrigatoriosVazios(string field)
    {
        var exception = Assert.Throws<ArgumentException>(() => field switch
        {
            "cidade" => CreateAddress(cidade: " "),
            "bairro" => CreateAddress(bairro: "\t"),
            "logradouro" => CreateAddress(logradouro: ""),
            "numero" => CreateAddress(numero: null),
            _ => throw new InvalidOperationException()
        });

        Assert.Equal(field, exception.ParamName);
    }

    [Fact]
    public void Criar_RejeitaCampoAcimaDoLimite()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateAddress(cidade: new string('A', 101)));

        Assert.Equal("cidade", exception.ParamName);
    }

    [Fact]
    public void ComplementoVazioEhNormalizadoParaNulo()
    {
        var address = CreateAddress(complemento: " \t ");

        Assert.Null(address.Complemento);
    }

    [Fact]
    public void AtualizarDados_NormalizaCamposEPreservaIdentificadoresEIndicadorDefault()
    {
        var id = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var address = CreateAddress(id: id, clienteUserId: clientId, isDefault: true);

        address.AtualizarDados(
            "95000-100",
            "sc",
            "  Florianópolis ",
            " Centro ",
            " Avenida   Central ",
            " 45 ",
            "  Bloco A ");

        Assert.Equal(id, address.Id);
        Assert.Equal(clientId, address.ClienteUserId);
        Assert.Equal("95000100", address.Cep);
        Assert.Equal("SC", address.Uf);
        Assert.Equal("Florianópolis", address.Cidade);
        Assert.Equal("Centro", address.Bairro);
        Assert.Equal("Avenida Central", address.Logradouro);
        Assert.Equal("45", address.Numero);
        Assert.Equal("Bloco A", address.Complemento);
        Assert.True(address.IsDefault);
    }

    [Fact]
    public void AtualizarDados_InvalidoPreservaDadosAnteriores()
    {
        var address = CreateAddress();

        Assert.Throws<ArgumentException>(() =>
            address.AtualizarDados("inválido", "RS", "Nova cidade", "Centro", "Rua A", "1", null));

        Assert.Equal("95000000", address.Cep);
        Assert.Equal("Vacaria", address.Cidade);
    }

    [Fact]
    public void IndicadorDefault_NaoPossuiSetterPublico()
    {
        var property = typeof(ClienteEndereco).GetProperty(nameof(ClienteEndereco.IsDefault));

        Assert.NotNull(property);
        Assert.Null(property!.GetSetMethod());
    }

    [Fact]
    public void DefinirComoPadrao_EhComportamentoInterno()
    {
        var method = typeof(ClienteEndereco).GetMethod(
            "DefinirComoPadrao",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
    }

    [Fact]
    public void ToString_NaoExpoeDadosDeEndereco()
    {
        var address = CreateAddress();

        Assert.Equal("[ClienteEndereco]", address.ToString());
        Assert.DoesNotContain("95000000", address.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Vacaria", address.ToString(), StringComparison.Ordinal);
    }

    private static ClienteEndereco CreateAddress(
        Guid? id = null,
        Guid? clienteUserId = null,
        string? cep = "95000-000",
        string? uf = "RS",
        string? cidade = "Vacaria",
        string? bairro = "Centro",
        string? logradouro = "Rua das Flores",
        string? numero = "10",
        string? complemento = null,
        bool isDefault = false) =>
        ClienteEndereco.Criar(
            id ?? Guid.NewGuid(),
            clienteUserId ?? Guid.NewGuid(),
            cep,
            uf,
            cidade,
            bairro,
            logradouro,
            numero,
            complemento,
            isDefault);
}
