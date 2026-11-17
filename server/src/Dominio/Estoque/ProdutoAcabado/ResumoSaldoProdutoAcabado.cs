namespace Xaquiamuni.Dominio.Estoque;

public sealed record ResumoSaldoProdutoAcabado
{
    internal ResumoSaldoProdutoAcabado(
        int saldoFisico,
        int saldoUtilizavel,
        int saldoDisponivel,
        int? estoqueMinimo)
    {
        SaldoFisico = saldoFisico;
        SaldoUtilizavel = saldoUtilizavel;
        SaldoDisponivel = saldoDisponivel;
        EstoqueMinimo = estoqueMinimo;
    }

    public int SaldoFisico { get; }
    public int SaldoUtilizavel { get; }
    public int SaldoReservado => 0;
    public int SaldoDisponivel { get; }
    public int? EstoqueMinimo { get; }

    public static ResumoSaldoProdutoAcabado Calcular(
        Xaquiamuni.Dominio.Entidades.ProdutoVariante? variante,
        PoliticaEstoqueProdutoVariante? politica,
        IEnumerable<LoteProdutoAcabado> lotes,
        IEnumerable<SaldoLoteProdutoAcabado> saldos,
        DateOnly dataReferencia)
    {
        ArgumentNullException.ThrowIfNull(variante);
        ArgumentNullException.ThrowIfNull(lotes);
        ArgumentNullException.ThrowIfNull(saldos);
        if (variante.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "A variante deve possuir um identificador válido.",
                nameof(variante));
        }

        if (politica is not null && politica.ProdutoVarianteId != variante.Id)
        {
            throw new ArgumentException(
                "A política informada não pertence à variante.",
                nameof(politica));
        }

        var lotesArray = lotes.ToArray();
        var saldosArray = saldos.ToArray();
        var lotesPorId = new Dictionary<Guid, LoteProdutoAcabado>();
        foreach (var lote in lotesArray)
        {
            if (lote.ProdutoVarianteId != variante.Id)
            {
                throw new ArgumentException(
                    "Todos os lotes devem pertencer à variante.",
                    nameof(lotes));
            }

            if (!lotesPorId.TryAdd(lote.Id, lote))
            {
                throw new InvalidOperationException("Há mais de um lote com a mesma identidade.");
            }
        }

        var saldosPorLote = new Dictionary<Guid, SaldoLoteProdutoAcabado>();
        foreach (var saldo in saldosArray)
        {
            if (saldo.ProdutoVarianteId != variante.Id)
            {
                throw new ArgumentException(
                    "Todos os saldos devem pertencer à variante.",
                    nameof(saldos));
            }

            if (!lotesPorId.ContainsKey(saldo.LoteId))
            {
                throw new ArgumentException(
                    "Todo saldo deve pertencer a um lote da variante.",
                    nameof(saldos));
            }

            if (!saldosPorLote.TryAdd(saldo.LoteId, saldo))
            {
                throw new InvalidOperationException("Há mais de um saldo para o mesmo lote.");
            }
        }

        try
        {
            var saldoFisico = 0;
            var saldoUtilizavel = 0;
            foreach (var lote in lotesArray)
            {
                if (!saldosPorLote.TryGetValue(lote.Id, out var saldo))
                {
                    continue;
                }

                saldoFisico = checked(saldoFisico + saldo.QuantidadeFisica);
                if (lote.EstaValidoEm(dataReferencia))
                {
                    saldoUtilizavel = checked(saldoUtilizavel + saldo.QuantidadeVendavel);
                }
            }

            return new ResumoSaldoProdutoAcabado(
                saldoFisico,
                saldoUtilizavel,
                variante.Ativa ? saldoUtilizavel : 0,
                politica?.EstoqueMinimo);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(saldos),
                "A soma das posições excede o limite de quantidade inteira suportado.");
        }
    }
}
