using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Tests;

public class ClientePerfilTests
{
    [Fact]
    public void Criar_ComDadosValidosNormalizaNomeEArmazenaCpfProtegido()
    {
        var userId = Guid.NewGuid();
        var protectedCpf = CreateProtectedCpf();

        var profile = ClientePerfil.Criar(userId, "  Ana\t  Maria \r\n Silva ", protectedCpf);

        Assert.Equal(userId, profile.UserId);
        Assert.Equal("Ana Maria Silva", profile.NomeCompleto);
        Assert.Same(protectedCpf, profile.CpfProtegido);
    }

    [Fact]
    public void Criar_RejeitaIdentificadorVazio()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ClientePerfil.Criar(Guid.Empty, "Ana Maria", CreateProtectedCpf()));

        Assert.Equal("userId", exception.ParamName);
        Assert.DoesNotContain("52998224725", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Criar_RejeitaNomeVazio(string? name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ClientePerfil.Criar(Guid.NewGuid(), name, CreateProtectedCpf()));

        Assert.Equal("nomeCompleto", exception.ParamName);
    }

    [Fact]
    public void Criar_RejeitaNomeAcimaDoLimite()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ClientePerfil.Criar(Guid.NewGuid(), new string('A', 151), CreateProtectedCpf()));

        Assert.Contains("limite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AtualizarNomeCompleto_NormalizaNovoNome()
    {
        var profile = ClientePerfil.Criar(Guid.NewGuid(), "Ana Silva", CreateProtectedCpf());

        profile.AtualizarNomeCompleto("  Maria   Souza  ");

        Assert.Equal("Maria Souza", profile.NomeCompleto);
    }

    [Fact]
    public void AtualizarNomeCompleto_InvalidoPreservaNomeAtual()
    {
        var profile = ClientePerfil.Criar(Guid.NewGuid(), "Ana Silva", CreateProtectedCpf());

        Assert.Throws<ArgumentException>(() => profile.AtualizarNomeCompleto(" "));

        Assert.Equal("Ana Silva", profile.NomeCompleto);
    }

    [Fact]
    public void RepresentacoesTextuais_NaoExibemDadosDoPerfilOuCpfProtegido()
    {
        var protectedCpf = CreateProtectedCpf();
        var profile = ClientePerfil.Criar(Guid.NewGuid(), "Ana Silva", protectedCpf);

        Assert.Equal("[ClientePerfil]", profile.ToString());
        Assert.Equal("[CPF protegido]", protectedCpf.ToString());
        Assert.DoesNotContain(protectedCpf.Ciphertext, profile.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(protectedCpf.Fingerprint, profile.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ana Silva", profile.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CpfProtegido_RejeitaValorQueNaoTemFormatoDeCiphertext()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CpfProtegido(
                "52998224725",
                Convert.ToBase64String(new byte[12]),
                Convert.ToBase64String(new byte[16]),
                new string('A', 64),
                "enc-v1",
                "fp-v1"));

        Assert.Equal("O CPF protegido é inválido.", exception.Message);
        Assert.DoesNotContain("52998224725", exception.Message, StringComparison.Ordinal);
    }

    private static CpfProtegido CreateProtectedCpf() =>
        new(
            Convert.ToBase64String(new byte[11]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[16]),
            new string('A', 64),
            "enc-v1",
            "fp-v1");
}
