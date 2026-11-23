using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public enum MotivoSemDescontoFrete { ModalidadeIncompativel, FreteZero, SemCampanhaElegivel }

public sealed record ResultadoDescontoFrete(Guid? CampanhaId, long OriginalCentavos, long DescontoCentavos,
    long LiquidoCentavos, MotivoSemDescontoFrete? Motivo = null);

public static class DescontosFrete
{
    // O subtotal é o preço efetivamente cobrado pelos itens, com as promoções de produto já aplicadas.
    public static ResultadoDescontoFrete Resolver(ModalidadeEntregaPedido modalidade, long freteOriginalCentavos,
        long subtotalProdutosCentavos, DateTimeOffset instante, IEnumerable<PromocaoFrete> campanhas)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(freteOriginalCentavos);
        ArgumentOutOfRangeException.ThrowIfNegative(subtotalProdutosCentavos);
        var elegivel = modalidade switch
        {
            ModalidadeEntregaPedido.EntregaPropria => ModalidadesPromocaoFrete.EntregaPropria,
            ModalidadeEntregaPedido.Correios => ModalidadesPromocaoFrete.CorreiosManual,
            _ => (ModalidadesPromocaoFrete)0
        };
        if (elegivel == 0) return new(null, freteOriginalCentavos, 0, freteOriginalCentavos,
            MotivoSemDescontoFrete.ModalidadeIncompativel);
        if (freteOriginalCentavos == 0) return new(null, 0, 0, 0, MotivoSemDescontoFrete.FreteZero);

        var melhor = campanhas.Where(c => (c.Modalidades & elegivel) != 0 && c.VigenteEm(instante)
                && subtotalProdutosCentavos >= c.SubtotalMinimoCentavos)
            .Select(c => (Campanha: c, Desconto: Calcular(c, freteOriginalCentavos)))
            .Where(x => x.Desconto > 0)
            .OrderByDescending(x => x.Desconto).ThenBy(x => x.Campanha.Id)
            .FirstOrDefault();
        return melhor.Campanha is null
            ? new(null, freteOriginalCentavos, 0, freteOriginalCentavos, MotivoSemDescontoFrete.SemCampanhaElegivel)
            : new(melhor.Campanha.Id, freteOriginalCentavos, melhor.Desconto, freteOriginalCentavos - melhor.Desconto);
    }

    private static long Calcular(PromocaoFrete campanha, long frete) => campanha.Tipo switch
    {
        // Meio centavo arredonda para cima. Int128 preserva a precisão em todo o intervalo de bigint.
        TipoDescontoFrete.Percentual => (long)(((Int128)frete * campanha.Valor + 5_000) / 10_000),
        TipoDescontoFrete.ValorFixo => Math.Min(campanha.Valor, frete),
        TipoDescontoFrete.FreteGratis => frete,
        _ => 0
    };
}
