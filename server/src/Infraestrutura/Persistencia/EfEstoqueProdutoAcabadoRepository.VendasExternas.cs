using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IGerenciarVendasExternas
{
    private IQueryable<VendaExterna> VendasComDetalhes() => _dbContext.Set<VendaExterna>()
        .Include(v => v.Itens).Include(v => v.Recebimentos).AsSingleQuery();

    public async Task<IReadOnlyList<ResumoVendaExterna>> ListarAsync(CanalVendaExterna? canal,
        SituacaoVendaExterna? situacao, int pagina, CancellationToken ct)
    {
        var vendas = await _dbContext.Set<VendaExterna>().AsNoTracking().Include(v => v.Recebimentos)
            .Where(v => (!canal.HasValue || v.Canal == canal) && (!situacao.HasValue || v.Situacao == situacao))
            .OrderByDescending(v => v.DataVenda).ThenByDescending(v => v.Id)
            .Skip((pagina - 1) * 50).Take(50).ToListAsync(ct);
        return vendas.Select(ResumoVendaExterna.De).ToArray();
    }

    public async Task<DetalheVendaExterna?> ConsultarAsync(Guid id, CancellationToken ct)
    {
        var venda = await VendasComDetalhes().AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
        if (venda is null) return null;
        var itens = venda.Itens.Select(i => i.Id).ToArray();
        var baixas = await _dbContext.Set<BaixaVendaExterna>().AsNoTracking()
            .Where(b => itens.Contains(b.ItemId)).OrderBy(b => b.ItemId).ThenBy(b => b.LoteId).ToListAsync(ct);
        return new(ResumoVendaExterna.De(venda), venda.Comprador, venda.DescricaoCanal, venda.Vencimento,
            venda.ResponsavelId, venda.CriadaEmUtc, venda.CanceladaPorId, venda.CanceladaEmUtc, venda.MotivoCancelamento,
            venda.Itens.OrderBy(i => i.Id).Select(i => new ItemVendaExternaDetalhe(i.Id, i.ProdutoId, i.VarianteId,
                i.NomeProduto, i.NomeVariante, i.Quantidade, i.PrecoUnitarioCentavos, i.DescontoCentavos, i.SubtotalCentavos)).ToArray(),
            venda.Recebimentos.OrderBy(r => r.RegistradoEmUtc).ThenBy(r => r.Id)
                .Select(r => new RecebimentoVendaExternaDetalhe(r.Id, r.Data, r.Meio.ToString(), r.ValorCentavos,
                    r.ResponsavelId, r.RegistradoEmUtc)).ToArray(),
            baixas.Select(b => new BaixaVendaExternaDetalhe(b.ItemId, b.LoteId, b.MovimentoId, b.Quantidade,
                b.MovimentoCancelamentoId)).ToArray());
    }

    public async Task<ResumoVendaExterna?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct)
    {
        var operacao = await _dbContext.Set<OperacaoVendaExterna>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.ResponsavelId == autor && o.Chave == chave && o.Tipo == "criar", ct);
        return operacao is null ? null : JsonSerializer.Deserialize<ResumoVendaExterna>(operacao.Resultado);
    }

    public Task<VendaExternaResultado> CriarAsync(CriarVendaExternaInput input, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarVendaAsync(null, "criar", input, autor, chave, async (_, agora, fingerprint) =>
        {
            if (input.Itens is not { Count: >= 1 and <= 50 } ||
                input.Itens.Any(i => i is null || i.VarianteId == Guid.Empty || i.ProdutoId == Guid.Empty) ||
                input.Itens.Select(i => i.VarianteId).Distinct().Count() != input.Itens.Count)
                throw new ArgumentException("Informe de um a 50 itens, com uma linha por variante.");

            var ids = input.Itens.Select(i => i.VarianteId).Order().ToArray();
            foreach (var id in ids) await AdquirirLockVarianteAsync(id, ct);
            // Read the clock after locks so the cutoff used by FEFO is not stale.
            agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            var variantes = await _dbContext.ProdutoVariantes.AsNoTracking().Include(v => v.Produto)
                .Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
            var itens = input.Itens.Select(i =>
            {
                if (!variantes.TryGetValue(i.VarianteId, out var variante) || variante.ProdutoId != i.ProdutoId)
                    throw new ArgumentException("O produto não corresponde à variante informada.");
                return new ItemVendaExterna(variante, i.Quantidade, i.PrecoUnitarioCentavos, i.DescontoCentavos);
            }).ToArray();
            var venda = VendaExterna.Criar(LerEnum<CanalVendaExterna>(input.Canal), input.DescricaoCanal,
                input.DataVenda, input.Comprador, input.Vencimento, itens, autor, agora);
            if (input.RecebimentoInicial is { } pagamento) RegistrarRecebimento(venda, pagamento, autor, chave, agora);
            venda.ValidarComprador();
            _dbContext.Add(venda);
            foreach (var item in itens.OrderBy(i => i.VarianteId))
            {
                var movimentos = await AplicarSaidaFefoAsync(variantes[item.VarianteId], item.Quantidade, autor,
                    "Venda externa", agora, fingerprint, indice => $"venda:{item.Id:N}:{indice}", ct);
                if (movimentos is null)
                    throw new InvalidOperationException($"Saldo livre insuficiente para {item.NomeProduto} — {item.NomeVariante}.");
                _dbContext.MovimentosProdutoAcabado.AddRange(movimentos);
                _dbContext.AddRange(movimentos.Select(m => new BaixaVendaExterna(item.Id, m)));
            }
            return venda;
        }, ct);

    public Task<VendaExternaResultado> ReceberAsync(Guid id, ReceberVendaExternaInput input, Guid autor,
        Guid chave, CancellationToken ct) =>
        ExecutarVendaAsync(id, "receber", input, autor, chave, (venda, agora, _) =>
        {
            _dbContext.Add(RegistrarRecebimento(venda!, input, autor, chave, agora));
            return Task.FromResult(venda!);
        }, ct);

    public Task<VendaExternaResultado> CancelarAsync(Guid id, CancelarVendaExternaInput input, Guid autor,
        Guid chave, CancellationToken ct) =>
        ExecutarVendaAsync(id, "cancelar", input, autor, chave, async (venda, agora, fingerprint) =>
        {
            var itens = venda!.Itens.ToDictionary(i => i.Id);
            foreach (var variante in itens.Values.Select(i => i.VarianteId).Distinct().Order())
                await AdquirirLockVarianteAsync(variante, ct);
            agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            venda.Cancelar(input.Motivo, autor, agora);
            var data = _calendario.ObterDataEm(agora);
            var baixas = await _dbContext.Set<BaixaVendaExterna>().Where(b => itens.Keys.Contains(b.ItemId)).ToListAsync(ct);
            if (itens.Values.Any(i => baixas.Where(b => b.ItemId == i.Id).Sum(b => (long)b.Quantidade) != i.Quantidade))
                throw new InvalidOperationException("As baixas originais da venda não puderam ser conciliadas.");
            foreach (var baixa in baixas.OrderBy(b => itens[b.ItemId].VarianteId).ThenBy(b => b.LoteId))
            {
                var variante = itens[baixa.ItemId].VarianteId;
                var lote = await _dbContext.LotesProdutoAcabado.AsNoTracking().SingleAsync(l => l.Id == baixa.LoteId, ct);
                var saldo = await _dbContext.SaldosProdutoAcabado
                    .SingleOrDefaultAsync(s => s.LoteId == baixa.LoteId && s.ProdutoVarianteId == variante, ct);
                var ativa = await _dbContext.ProdutoVariantes.AnyAsync(v => v.Id == variante && v.Ativa, ct);
                var historico = await _dbContext.MovimentosProdutoAcabado.AsNoTracking()
                    .Where(m => m.LoteId == baixa.LoteId && m.ProdutoVarianteId == variante).ToListAsync(ct);
                var original = historico.SingleOrDefault(m => m.Id == baixa.MovimentoId);
                if (!ativa || saldo is null || !lote.EstaValidoEm(data) || original is null ||
                    historico.Any(m => m.Id != original.Id && m.RegistradoEmUtc >= original.RegistradoEmUtc))
                    throw new InvalidOperationException("O lote original está inelegível ou foi movido após a venda. O cancelamento não foi aplicado.");
                var compensacao = saldo.Estornar(lote, original, historico, autor, venda.MotivoCancelamento, agora,
                    $"cancelar-venda:{baixa.MovimentoId:N}", fingerprint);
                _dbContext.MovimentosProdutoAcabado.Add(compensacao);
                baixa.VincularCancelamento(compensacao);
            }
            return venda;
        }, ct);

    private async Task<VendaExternaResultado> ExecutarVendaAsync<T>(Guid? id, string tipo, T input, Guid autor, Guid chave,
        Func<VendaExterna?, DateTimeOffset, byte[], Task<VendaExterna>> aplicar, CancellationToken ct)
    {
        if (id == Guid.Empty || autor == Guid.Empty || chave == Guid.Empty)
            return new(VendaExternaStatus.Invalida, Erro: "Informe identificadores válidos.");
        var fingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, tipo, input }));
        var impressao = Convert.ToHexString(fingerprint);
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AdquirirLockAsync($"external-sale-key:{autor:D}:{chave:D}", ct);
            var anterior = await _dbContext.Set<OperacaoVendaExterna>().AsNoTracking()
                .SingleOrDefaultAsync(o => o.ResponsavelId == autor && o.Chave == chave, ct);
            if (anterior is not null)
                return anterior.Fingerprint == impressao
                    ? new(VendaExternaStatus.Repetida, JsonSerializer.Deserialize<ResumoVendaExterna>(anterior.Resultado))
                    : new(VendaExternaStatus.Conflito, Erro: "A chave já foi usada com outros dados.");

            VendaExterna? venda = null;
            if (id.HasValue)
            {
                await AdquirirLockAsync($"external-sale:{id:D}", ct);
                venda = await VendasComDetalhes().SingleOrDefaultAsync(v => v.Id == id, ct);
                if (venda is null) return new(VendaExternaStatus.Ausente);
            }
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            venda = await aplicar(venda, agora, fingerprint);
            var resumo = ResumoVendaExterna.De(venda);
            _dbContext.Add(new OperacaoVendaExterna(autor, chave, venda.Id, tipo, impressao, JsonSerializer.Serialize(resumo)));
            await _dbContext.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return new(VendaExternaStatus.Sucesso, resumo);
        }
        catch (ArgumentException erro) { return new(VendaExternaStatus.Invalida, Erro: erro.Message); }
        catch (OverflowException) { return new(VendaExternaStatus.Invalida, Erro: "Os valores excedem o limite suportado."); }
        catch (InvalidOperationException erro) { return new(VendaExternaStatus.Conflito, Erro: erro.Message); }
        catch (DbUpdateException erro) when (erro is DbUpdateConcurrencyException || IsUniqueViolation(erro))
        { return new(VendaExternaStatus.Conflito, Erro: "Outra operação alterou a venda ou o estoque."); }
        catch (SqlException erro) when (erro.Number is 1205 or 1222 or 51000)
        { return new(VendaExternaStatus.Conflito, Erro: "Outra operação está alterando o estoque. Repita com a mesma chave."); }
        finally { _dbContext.ChangeTracker.Clear(); }
    }

    private static RecebimentoVendaExterna RegistrarRecebimento(VendaExterna venda, ReceberVendaExternaInput input,
        Guid autor, Guid chave, DateTimeOffset agora) =>
        venda.Receber(input.ValorCentavos, input.Data, LerEnum<MeioRecebimentoVendaExterna>(input.Meio), autor, chave, agora);

    private static T LerEnum<T>(string texto) where T : struct, Enum =>
        Enum.TryParse<T>(texto, out var valor) && Enum.IsDefined(valor) && valor.ToString() == texto
            ? valor : throw new ArgumentException($"Informe {string.Join(", ", Enum.GetNames<T>())}.");
}

internal sealed record OperacaoVendaExterna(Guid ResponsavelId, Guid Chave, Guid VendaId, string Tipo,
    string Fingerprint, string Resultado);
