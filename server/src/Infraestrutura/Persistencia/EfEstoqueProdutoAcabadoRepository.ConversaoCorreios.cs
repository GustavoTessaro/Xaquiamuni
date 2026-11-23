using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : IRevisarConversaoCorreios
{
    private sealed record CondicaoPostal(Guid ClienteId, Guid CotacaoId, int Revisao, string Precos);

    private sealed record ConversaoPostal(SolicitacaoCorreios Solicitacao, PropostaCorreios Proposta)
    {
        public ItemCongeladoCorreios[] Itens { get; } = LerPostal<ItemCongeladoCorreios[]>(Solicitacao.ItensJson);
        public EnderecoEntregaPedido Endereco { get; } = LerPostal<EnderecoEntregaPedido>(Solicitacao.EnderecoJson);
        public DadosPropostaPostal Snapshot { get; } = new(Solicitacao.Id, Proposta.Revisao, Proposta.Servico,
            Proposta.PrazoDias, Proposta.PublicadaEmUtc, Proposta.ValidaAteUtc);

        public ItemCompraInput[] ItensCompra() =>
            Itens.OrderBy(x => x.ProdutoVarianteId).Select(x => new ItemCompraInput(x.ProdutoId, x.ProdutoVarianteId, x.Quantidade)).ToArray();
    }

    public async Task<CriarPedidoCompraResult> RevisarAsync(Guid clienteId, Guid cotacaoId, CancellationToken ct)
    {
        try
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await BloquearSolicitacaoPostalAsync(cotacaoId, ct);
            var (postal, falha) = await CarregarConversaoPostalAsync(clienteId, cotacaoId, ct);
            if (postal is null) return new(falha);
            if (_promocoesHabilitadas)
                await PromocoesComerciaisSql.BloquearAsync(_dbContext, escrita: false, ct);
            var itens = postal.ItensCompra();
            foreach (var item in itens) await AdquirirLockVarianteAsync(item.ProdutoVarianteId, ct);
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            if (postal.Proposta.ValidaAteUtc <= _timeProvider.GetUtcNow()) return new(CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
            if (!await ClienteAptoAsync(clienteId, agora, ct)) return new(CriarPedidoCompraStatus.ClienteInapto);
            var indisponiveis = new List<Guid>();
            var (dados, precos) = await CarregarItensCompraAsync(itens, agora, ct, indisponiveis);
            if (dados is null) return new(CriarPedidoCompraStatus.ItemNaoCompravel, ItensIndisponiveis: indisponiveis);
            if (!TentarCalcularTotalCompra(dados, postal.Proposta.FreteCentavos, out var subtotal, out var total))
                return new(CriarPedidoCompraStatus.LimiteMonetarioExcedido);

            // Planeja com o mesmo critério da venda. Nenhuma entidade é adicionada ao contexto.
            await PlanejarReservasCompraAsync(Guid.NewGuid(), dados, agora, null, indisponiveis, ct);
            if (postal.Proposta.ValidaAteUtc <= _timeProvider.GetUtcNow()) return new(CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
            return new(CriarPedidoCompraStatus.Sucesso, RevisaoPostal:
                MontarRevisaoPostal(postal, dados, precos!, subtotal, total, indisponiveis));
        }
        catch (SqlException e) when (e.Number is 1205 or 1222 or 51000)
        {
            return new(CriarPedidoCompraStatus.ConflitoConcorrencia);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<(ConversaoPostal? Postal, CriarPedidoCompraStatus Status)> CarregarConversaoPostalAsync(
        Guid clienteId, Guid cotacaoId, CancellationToken ct)
    {
        var solicitacao = await SolicitacoesPostais.SingleOrDefaultAsync(x => x.Id == cotacaoId && x.ClienteId == clienteId, ct);
        if (solicitacao is null) return (null, CriarPedidoCompraStatus.CotacaoCorreiosNaoEncontrada);
        var situacao = solicitacao.SituacaoEm(_timeProvider.GetUtcNow());
        if (situacao == SituacaoCotacaoCorreios.ConvertidaEmPedido) return (null, CriarPedidoCompraStatus.CotacaoCorreiosConvertida);
        if (situacao == SituacaoCotacaoCorreios.Expirada) return (null, CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
        if (situacao != SituacaoCotacaoCorreios.Aceita || solicitacao.RevisaoAceita is not { } revisao ||
            revisao != solicitacao.Revisao) return (null, CriarPedidoCompraStatus.CotacaoCorreiosNaoAceita);
        var proposta = await _dbContext.Set<PropostaCorreios>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.SolicitacaoId == cotacaoId && x.Revisao == revisao, ct);
        if (proposta is null) return (null, CriarPedidoCompraStatus.ConversaoCorreiosInvalida);
        if (proposta.ValidaAteUtc <= _timeProvider.GetUtcNow()) return (null, CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
        return (new(solicitacao, proposta), CriarPedidoCompraStatus.Sucesso);
    }

    private CondicaoPostal? LerCondicaoPostal(string token)
    {
        try
        {
            var json = _conversoesCorreios?.Unprotect(token);
            return json is null ? null : LerPostal<CondicaoPostal>(json);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private RevisaoConversaoCorreios MontarRevisaoPostal(ConversaoPostal postal, DadosItemPedido[] dados,
        PrecoAtualPedido[] precos, long subtotal, long total, IReadOnlyCollection<Guid> indisponiveis)
    {
        var porVariante = precos.ToDictionary(x => x.ProdutoVarianteId);
        var itens = postal.Itens.Select(x => new ItemRevisaoConversaoCorreios(x.ProdutoId, x.ProdutoVarianteId, x.Quantidade,
            x.NomeProduto, x.NomeVariante, !indisponiveis.Contains(x.ProdutoVarianteId), porVariante[x.ProdutoVarianteId])).ToArray();
        var condicao = new CondicaoPostal(postal.Solicitacao.ClienteId, postal.Solicitacao.Id, postal.Proposta.Revisao,
            IdentificarCarrinho(dados));
        return new(postal.Snapshot, postal.Endereco, itens, postal.Proposta.FreteCentavos, subtotal, total,
            indisponiveis.Count == 0 ? _conversoesCorreios?.Protect(SerializarPostal(condicao)) : null);
    }

    private static bool TentarCalcularTotalCompra(IEnumerable<DadosItemPedido> dados, long frete, out long subtotal, out long total)
    {
        subtotal = total = 0;
        try
        {
            subtotal = dados.Sum(x => checked(x.PrecoUnitarioCentavos * x.Quantidade));
            total = checked(subtotal + frete);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
