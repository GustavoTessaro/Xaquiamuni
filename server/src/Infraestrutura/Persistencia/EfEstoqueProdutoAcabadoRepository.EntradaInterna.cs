using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository
{
    private async Task<LoteProdutoAcabado> CriarLoteEntradaAsync(ProdutoVariante variante, DateOnly fabricacao,
        DateOnly? validade, CancellationToken ct)
    {
        var politica = await _dbContext.PoliticasEstoqueProdutoVariante
            .SingleOrDefaultAsync(p => p.ProdutoVarianteId == variante.Id, ct);
        return LoteProdutoAcabado.Criar(variante, politica, fabricacao, validade);
    }

    private MovimentoProdutoAcabado AplicarEntradaAcabado(SaldoLoteProdutoAcabado saldo, LoteProdutoAcabado lote,
        int quantidade, CondicaoEstoqueProdutoAcabado condicao, TipoMovimentoProdutoAcabado tipo, Guid autor,
        string? motivo, DateTimeOffset agora, string chave, byte[] fingerprint, string? observacao)
    {
        var movimento = saldo.RegistrarEntrada(lote, quantidade, condicao, tipo, autor, motivo, agora,
            chave, fingerprint, observacao);
        _dbContext.MovimentosProdutoAcabado.Add(movimento);
        return movimento;
    }

    // O chamador detém o lock da variante e controla a transação.
    internal async Task<MovimentoProdutoAcabado> AplicarEntradaProducaoAsync(Guid varianteId, int quantidade,
        DateOnly? fabricacao, DateOnly? validade, DateOnly dataOperacional, DateTimeOffset agora,
        Guid autor, Guid ordemId, byte[] fingerprint, CancellationToken ct)
    {
        var variante = await _dbContext.ProdutoVariantes
            .FromSqlInterpolated($"SELECT * FROM [ProdutoVariantes] WITH (HOLDLOCK) WHERE [Id] = {varianteId}")
            .AsNoTracking().SingleAsync(ct);
        if (!variante.Ativa) throw new ArgumentException("A variante está inativa.");
        var dataFabricacao = fabricacao ?? dataOperacional;
        if (dataFabricacao == default || dataFabricacao > dataOperacional)
            throw new ArgumentException("Informe uma data de fabricação válida, sem usar uma data futura.");
        var lote = await CriarLoteEntradaAsync(variante, dataFabricacao, validade, ct);
        if (!lote.EstaValidoEm(dataOperacional))
            throw new ArgumentException("O lote produzido precisa estar dentro da validade.");
        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        _dbContext.LotesProdutoAcabado.Add(lote);
        _dbContext.SaldosProdutoAcabado.Add(saldo);
        return AplicarEntradaAcabado(saldo, lote, quantidade, CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual, autor, "Entrada de produção", agora,
            $"producao:{Guid.NewGuid():N}", fingerprint, $"Ordem de produção {ordemId:D}");
    }
}
