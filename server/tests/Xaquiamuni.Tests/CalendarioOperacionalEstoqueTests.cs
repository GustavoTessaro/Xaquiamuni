using Microsoft.Extensions.Options;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public sealed class CalendarioOperacionalEstoqueTests
{
    [Fact]
    public void OperationalDate_UsesConfiguredTimeZoneAcrossUtcMidnight()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 11, 17, 2, 59, 59, TimeSpan.Zero));
        var calendar = new CalendarioOperacionalEstoque(
            clock,
            Options.Create(new CalendarioOperacionalEstoqueOptions
            {
                TimeZoneId = "America/Sao_Paulo"
            }));

        Assert.Equal(new DateOnly(2026, 11, 16), calendar.ObterDataAtual());

        clock.SetUtcNow(new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 17), calendar.ObterDataAtual());
    }

    [Fact]
    public void TimeZoneValidation_RejectsMissingAndUnknownIdentifiers()
    {
        Assert.False(CalendarioOperacionalEstoque.ZonaValida(null));
        Assert.False(CalendarioOperacionalEstoque.ZonaValida("Not/A_Real_Zone"));
        Assert.True(CalendarioOperacionalEstoque.ZonaValida("America/Sao_Paulo"));
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _currentTime;

        public void SetUtcNow(DateTimeOffset value) => _currentTime = value.ToUniversalTime();
    }
}
