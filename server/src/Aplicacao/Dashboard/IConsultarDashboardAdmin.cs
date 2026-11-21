namespace Xaquiamuni.Aplicacao.Dashboard;

public sealed record OperacaoDashboard(
    int PixNoPrazo, int PixVencido, int APreparar, int EmPreparacao, int ProntosParaRetirada,
    int ProducoesEmAndamento, int ComprasRascunho);

public sealed record EstoqueDashboard(int MateriasPrimasEmAlerta, int ProdutosDisponiveis);
public sealed record CompromissoDashboard(DateOnly Data, TimeOnly? HorarioInicial, string Titulo);

public interface IConsultarDashboardAdmin
{
    Task<OperacaoDashboard> OperacaoAsync(CancellationToken ct);
    Task<EstoqueDashboard> EstoqueAsync(CancellationToken ct);
    Task<IReadOnlyList<CompromissoDashboard>> AgendaAsync(CancellationToken ct);
}
