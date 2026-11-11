namespace Xaquiamuni.Infraestrutura.Outbox;

public sealed class EmailOutboxBackgroundServiceOptions
{
    public const string SectionName = "EmailOutboxWorker";
    public static readonly TimeSpan MinimumCycleInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumCycleInterval = TimeSpan.FromMinutes(5);
    public const int MinimumBatchSize = 1;
    public const int MaximumBatchSize = 100;

    public bool Enabled { get; set; }
    public TimeSpan CycleInterval { get; set; } = TimeSpan.FromSeconds(15);
    public int BatchSize { get; set; } = 10;
}
