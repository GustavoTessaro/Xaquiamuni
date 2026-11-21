using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfPublicacaoTemaLoja(XaquiamuniDbContext db, TimeProvider relogio) : IPublicacaoTemaLoja
{
    public async Task<PublicacaoTemaLoja> ConsultarAsync(CancellationToken ct) =>
        Mapear(await db.ConfiguracoesTemaLoja.AsNoTracking().SingleOrDefaultAsync(ct));

    public async Task<PublicacaoTemaLoja?> PublicarAsync(string chave, int revisaoEsperada, Guid administradorId,
        Guid idempotencia, CancellationToken ct)
    {
        if (TemasLoja.Buscar(chave)?.Disponivel != true)
            throw new ArgumentException("Tema indisponível.", nameof(chave));

        var atual = await db.ConfiguracoesTemaLoja.SingleOrDefaultAsync(ct);
        if (Repeticao(atual, chave, revisaoEsperada, administradorId, idempotencia))
            return Mapear(atual);
        if (atual?.UltimaChaveIdempotencia == idempotencia || (atual?.Revisao ?? 0) != revisaoEsperada)
            return null;

        var nova = atual is null;
        var configuracao = atual ?? new ConfiguracaoTemaLoja();
        configuracao.Publicar(chave, administradorId, idempotencia, relogio.GetUtcNow());
        if (nova) db.ConfiguracoesTemaLoja.Add(configuracao);
        try
        {
            await db.SaveChangesAsync(ct);
            return Mapear(configuracao);
        }
        catch (DbUpdateException erro) when (erro is DbUpdateConcurrencyException ||
            (nova && erro.InnerException is SqlException { Number: 2601 or 2627 }))
        {
            db.Entry(configuracao).State = EntityState.Detached;
            var vencedora = await db.ConfiguracoesTemaLoja.AsNoTracking().SingleOrDefaultAsync(ct);
            return Repeticao(vencedora, chave, revisaoEsperada, administradorId, idempotencia)
                ? Mapear(vencedora) : null;
        }
    }

    private static bool Repeticao(ConfiguracaoTemaLoja? config, string chave, int revisao, Guid administrador, Guid idempotencia) =>
        config is not null && config.Revisao - 1 == revisao && config.Chave == chave &&
        config.AdministradorId == administrador && config.UltimaChaveIdempotencia == idempotencia;

    private static PublicacaoTemaLoja Mapear(ConfiguracaoTemaLoja? config) => config is null
        ? PublicacaoTemaLoja.Inicial
        : new(config.Chave, config.Revisao, config.PublicadoEmUtc, config.AdministradorId);
}
