using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record PrecoPromocional(long Regular, long Final, int OffPontosBase)
{
    public long Economia => Regular - Final;
}

public static class PrecosPromocionais
{
    public static PrecoPromocional? Calcular(long regular, TipoPromocao tipo, long valor)
    {
        if (regular <= 0 || valor <= 0) return null;
        var final = tipo switch
        {
            TipoPromocao.Percentual when valor < 10_000 =>
                (long)decimal.Round(regular * (10_000m - valor) / 10_000m, 0, MidpointRounding.AwayFromZero),
            TipoPromocao.PrecoFixo => valor,
            _ => 0
        };
        return final > 0 && final < regular
            ? new(regular, final, (int)decimal.Floor((regular - final) * 10_000m / regular))
            : null;
    }

    public static (Promocao? Promocao, PrecoPromocional? Preco, bool EspecificaPrevalece) Resolver(
        IEnumerable<Promocao> promocoes, Guid varianteId, long regular, DateTimeOffset instante)
    {
        var elegiveis = promocoes.Where(p => p.VigenteEm(instante) && p.Abrange(varianteId))
            .Select(p => (Promocao: p, Preco: Calcular(regular, p.Tipo, p.Valor)))
            .Where(x => x.Preco is not null)
            .OrderByDescending(x => x.Promocao.Escopo == EscopoPromocao.VariantesSelecionadas)
            .ThenBy(x => x.Promocao.Id).ToArray();
        var (promocao, preco) = elegiveis.FirstOrDefault();
        return (promocao, preco, promocao?.Escopo == EscopoPromocao.VariantesSelecionadas &&
            elegiveis.Any(x => x.Promocao.Escopo == EscopoPromocao.TodasVariantes));
    }
}
