using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class FornecedorTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 19, 8, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void NormalizaCamposEPreservaIdentidadeEAuditoria()
    {
        var fornecedor = Fornecedor.Criar("  Ervas  do Vale ", " +55 (11) 90000-0000 ", " ", " notas ", Agora);
        var id = fornecedor.Id;
        Assert.NotEqual(Guid.Empty, id);
        Assert.True(fornecedor.Ativo);
        Assert.Equal("Ervas do Vale", fornecedor.Nome);
        Assert.Equal("ERVAS DO VALE", fornecedor.NomeNormalizado);
        Assert.Equal("+55 (11) 90000-0000", fornecedor.Telefone);
        Assert.Null(fornecedor.Email);
        Assert.Equal("notas", fornecedor.Observacoes);
        Assert.Equal(TimeSpan.Zero, fornecedor.CriadoEmUtc.Offset);

        fornecedor.Editar("Vale", null, "contato@example.test", null, Agora.AddHours(1));
        fornecedor.DefinirAtivo(false, Agora.AddHours(2));
        fornecedor.DefinirAtivo(false, Agora.AddHours(3));

        Assert.Equal(id, fornecedor.Id);
        Assert.False(fornecedor.Ativo);
        Assert.Equal("contato@example.test", fornecedor.Email);
        Assert.Equal(Agora.ToUniversalTime(), fornecedor.CriadoEmUtc);
        Assert.Equal(Agora.AddHours(2).ToUniversalTime(), fornecedor.AtualizadoEmUtc);
    }

    [Theory]
    [InlineData(" ", null, null)]
    [InlineData("Vale", "123", null)]
    [InlineData("Vale", "telefone 999999999", null)]
    [InlineData("Vale", null, "sem-arroba")]
    public void RejeitaDadosInvalidos(string nome, string? telefone, string? email) =>
        Assert.Throws<ArgumentException>(() => Fornecedor.Criar(nome, telefone, email, null, Agora));

    [Fact]
    public void RejeitaObservacoesAcimaDoLimite() =>
        Assert.Throws<ArgumentException>(() =>
            Fornecedor.Criar("Vale", null, null, new string('x', Fornecedor.ObservacoesMax + 1), Agora));
}
