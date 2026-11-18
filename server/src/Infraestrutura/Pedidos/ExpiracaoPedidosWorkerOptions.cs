namespace Xaquiamuni.Infraestrutura.Pedidos;

public sealed class ExpiracaoPedidosWorkerOptions
{
    public const string SectionName = "ExpiracaoPedidosWorker";

    public bool Enabled { get; set; }
    public TimeSpan CycleInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int BatchSize { get; set; } = 50;
    public int MaxPagesPerCycle { get; set; } = 4;
}
