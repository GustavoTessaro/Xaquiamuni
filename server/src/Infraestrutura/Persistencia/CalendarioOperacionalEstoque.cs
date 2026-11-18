using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class CalendarioOperacionalEstoque : ICalendarioOperacionalEstoque
{
    public const string SectionName = "Estoque:Calendario";
    public const string ZonaPadraoProposta = "America/Sao_Paulo";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public CalendarioOperacionalEstoque(
        TimeProvider timeProvider,
        IOptions<CalendarioOperacionalEstoqueOptions> options)
    {
        _timeProvider = timeProvider;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);
    }

    public DateOnly ObterDataAtual() => ObterDataEm(_timeProvider.GetUtcNow());

    public DateOnly ObterDataEm(DateTimeOffset instanteUtc)
    {
        var instanteLocal = TimeZoneInfo.ConvertTime(instanteUtc, _timeZone);
        return DateOnly.FromDateTime(instanteLocal.DateTime);
    }

    public static bool ZonaValida(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

public sealed class CalendarioOperacionalEstoqueOptions
{
    public string TimeZoneId { get; set; } = CalendarioOperacionalEstoque.ZonaPadraoProposta;
}
