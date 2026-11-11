namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

public static class EmailOutboxRetryPolicy
{
    public const int MaximumAttempts = 5;
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(15);

    private const double MinimumJitterMultiplier = 0.8;

    public static TimeSpan GetRetryDelay(int attempt, double jitterSample)
    {
        if (attempt < 1 || attempt >= MaximumAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        if (!double.IsFinite(jitterSample) || jitterSample is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterSample));
        }

        var exponentialTicks = Math.Min(
            BaseDelay.Ticks * Math.Pow(2, attempt - 1),
            MaximumDelay.Ticks);
        var minimumTicks = (long)Math.Ceiling(exponentialTicks * MinimumJitterMultiplier);
        var jitteredTicks = minimumTicks
            + (long)Math.Round(
                (exponentialTicks - minimumTicks) * jitterSample,
                MidpointRounding.AwayFromZero);

        return TimeSpan.FromTicks(jitteredTicks);
    }
}
