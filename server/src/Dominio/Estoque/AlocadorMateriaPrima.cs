namespace Xaquiamuni.Dominio.Estoque;

public sealed record PosicaoParaReserva(Guid SaldoId, Guid? LoteId, DateOnly? DataEntrada,
    DateOnly? Validade, bool Elegivel, decimal Fisico, decimal Reservado);
public sealed record ParcelaReservaMateriaPrima(Guid SaldoId, decimal Quantidade);

public static class AlocadorMateriaPrima
{
    public static IReadOnlyList<ParcelaReservaMateriaPrima>? Planejar(QuantidadeEstoque necessidade,
        bool controlaLote, IEnumerable<PosicaoParaReserva> posicoes)
    {
        var restante = necessidade.ExigirPositiva().ValorBase;
        var parcelas = new List<ParcelaReservaMateriaPrima>();
        var verificadas = new HashSet<Guid>();
        foreach (var posicao in posicoes.Where(p => p.Elegivel && p.LoteId.HasValue == controlaLote)
            .OrderBy(p => !p.Validade.HasValue).ThenBy(p => p.Validade)
            .ThenBy(p => p.DataEntrada).ThenBy(p => p.LoteId).ThenBy(p => p.SaldoId))
        {
            if (!verificadas.Add(posicao.SaldoId))
                throw new ArgumentException("Uma posição de saldo apareceu mais de uma vez.");
            var livre = Math.Max(0m, posicao.Fisico - posicao.Reservado);
            if (livre == 0m) continue;
            var quantidade = QuantidadeEstoque.Criar(Math.Min(restante, livre), necessidade.UnidadeBase).ValorBase;
            parcelas.Add(new(posicao.SaldoId, quantidade));
            restante -= quantidade;
            if (restante == 0m) return parcelas;
        }
        return null;
    }
}
