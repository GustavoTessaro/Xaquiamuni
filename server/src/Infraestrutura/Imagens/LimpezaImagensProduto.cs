using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Infraestrutura.Imagens;

internal sealed record LimpezaImagemProduto(Guid Id, string Chave, DateTimeOffset ProximaTentativaUtc);

public sealed class LimpezaImagensProduto(
    XaquiamuniDbContext db, IArmazenamentoImagemProduto armazenamento, TimeProvider relogio,
    ILogger<LimpezaImagensProduto> logger)
{
    public async Task ProcessarAsync(CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        var agora = relogio.GetUtcNow();
        var ids = await db.Set<LimpezaImagemProduto>().AsNoTracking()
            .Where(p => p.ProximaTentativaUtc <= agora)
            .OrderBy(p => p.ProximaTentativaUtc).ThenBy(p => p.Id)
            .Select(p => p.Id).Take(32).ToArrayAsync(ct);
        foreach (var id in ids)
            await ProcessarAsync(id, ct);
    }

    private async Task ProcessarAsync(Guid id, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var pendente = await db.Set<LimpezaImagemProduto>().FromSqlInterpolated($"""
            SELECT * FROM [LimpezasImagensProdutos] WITH (UPDLOCK)
            WHERE [Id] = {id} AND [ProximaTentativaUtc] <= {relogio.GetUtcNow()}
            """).SingleOrDefaultAsync(ct);
        if (pendente is null) return;

        var chave = pendente.Chave;
        if (ChaveArmazenamentoImagemProduto.EhValida(chave)
            && !await db.ProdutoImagens.AnyAsync(i =>
                EF.Functions.Collate(i.ChaveArmazenamento, "Latin1_General_100_CI_AS") == chave, ct))
        {
            try
            {
                // O SQL protege a consulta de referências até o armazenamento terminar.
                await armazenamento.ExcluirAsync(chave, ct);
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(erro, "A limpeza da imagem {LimpezaId} será tentada novamente.", id);
                db.Entry(pendente).Property(p => p.ProximaTentativaUtc).CurrentValue = relogio.GetUtcNow().AddMinutes(1);
                await db.SaveChangesAsync(ct);
                await transacao.CommitAsync(ct);
                return;
            }
        }

        db.Remove(pendente);
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }
}

internal sealed class LimpezaImagensProdutoBackgroundService(
    IServiceScopeFactory escopos, ILogger<LimpezaImagensProdutoBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<LimpezaImagensProduto>().ProcessarAsync(stoppingToken);
            }
            catch (Exception erro) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(erro, "O processamento das limpezas de imagens será retomado na próxima execução.");
            }
        }
    }
}
