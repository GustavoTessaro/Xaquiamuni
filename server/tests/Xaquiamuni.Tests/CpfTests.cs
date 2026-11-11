using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Tests;

public class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("52998224725", "52998224725")]
    public void TryCreate_AcceptsValidCpfAndExposesNormalizedDigits(string input, string expectedDigits)
    {
        Assert.True(Cpf.TryCreate(input, out var cpf));
        Assert.Equal(expectedDigits, cpf!.GetNormalizedDigitsForProtection());
    }

    [Fact]
    public void TryCreate_NormalizesFormattedAndUnformattedCpfToEqualValues()
    {
        Assert.True(Cpf.TryCreate("529.982.247-25", out var formatted));
        Assert.True(Cpf.TryCreate("52998224725", out var unformatted));
        var formattedCpf = Assert.IsType<Cpf>(formatted);
        var unformattedCpf = Assert.IsType<Cpf>(unformatted);

        Assert.Equal(formattedCpf, unformattedCpf);
        Assert.Equal(formattedCpf.GetHashCode(), unformattedCpf.GetHashCode());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("5299822472")]
    [InlineData("529982247250")]
    [InlineData("529.982.24725")]
    [InlineData("529982247-25")]
    [InlineData("529.982.24A-25")]
    [InlineData("５２９９８２２４７２５")]
    [InlineData("529.982.247-24")]
    [InlineData("529.982.247-26")]
    [InlineData("000.000.000-00")]
    [InlineData("11111111111")]
    public void TryCreate_RejectsInvalidInputWithoutReturningCpf(string? input)
    {
        Assert.False(Cpf.TryCreate(input, out var cpf));
        Assert.Null(cpf);
    }

    [Fact]
    public void CpfRepresentation_DoesNotRevealDigits()
    {
        Assert.True(Cpf.TryCreate("529.982.247-25", out var cpf));
        var validCpf = Assert.IsType<Cpf>(cpf);

        Assert.Equal("[CPF]", validCpf.ToString());
        Assert.DoesNotContain("52998224725", validCpf.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetNormalizedDigitsForProtection_IsExplicitlyNamed()
    {
        Assert.True(Cpf.TryCreate("529.982.247-25", out var cpf));

        Assert.Equal("52998224725", cpf!.GetNormalizedDigitsForProtection());
    }
}
