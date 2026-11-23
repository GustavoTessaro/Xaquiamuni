using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EntregaCheckoutOptions
{
    public Dictionary<string, string> MunicipiosPorCep { get; init; } = [];
}

public sealed partial class EfEstoqueProdutoAcabadoRepository : ICotarEntrega
{
    private sealed record OfertaProtegida(Guid ClienteId, string Carrinho, DateTimeOffset ValidaAteUtc,
        int RevisaoConfiguracao, Guid? EnderecoCadastradoId, int? RevisaoCidade,
        long Frete, DadosEntregaPedido Dados);

    private Task BloquearConfiguracaoEntregaAsync(CancellationToken ct) =>
        AdquirirLockAsync(EfConfiguracoesAtendimento.RecursoLock, ct, compartilhado: true);

    public async Task<CotacaoEntregaResult> CotarAsync(Guid clienteId, IReadOnlyList<ItemCompraInput> itens,
        Guid? enderecoCadastradoId, EnderecoCotacaoEntrega? endereco, bool usarEnderecoPadrao, CancellationToken ct)
    {
        if (_ofertasEntrega is null)
            return new([], [], "cotacao_indisponivel");

        try
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await BloquearConfiguracaoEntregaAsync(ct);
            if (_promocoesHabilitadas)
                await PromocoesComerciaisSql.BloquearAsync(_dbContext, escrita: false, ct);

            var agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            if (!await ClienteAptoAsync(clienteId, agora, ct))
                return new([], [], "cliente_inapto");

            var (dados, precos) = await CarregarItensCompraAsync(itens, agora, ct);
            if (dados is null)
                return new([], [], "itens_indisponiveis");

            var configuracao = await CarregarConfiguracaoAsync(ct);
            var carrinho = IdentificarCarrinho(dados);
            var validade = agora.AddMinutes(5);
            var ofertas = new List<OfertaEntregaResponse>();
            long subtotal;
            try
            {
                subtotal = dados.Sum(item => checked(item.PrecoUnitarioCentavos * item.Quantidade));
            }
            catch (OverflowException)
            {
                return new([], precos!, "limite_monetario_excedido");
            }

            if (configuracao.RetiradaHabilitada)
                AdicionarOferta(new(EnderecoRetirada: configuracao.EnderecoRetirada,
                    InstrucoesRetirada: configuracao.InstrucoesRetirada), 0);

            string? motivo = "entrega_propria_desabilitada";
            if (configuracao.EntregaPropriaHabilitada)
            {
                if (usarEnderecoPadrao)
                    enderecoCadastradoId = await _dbContext.ClienteEnderecos
                        .Where(item => item.ClienteUserId == clienteId && item.IsDefault)
                        .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(ct);
                var destino = await ResolverDestinoAsync(clienteId, enderecoCadastradoId, endereco, ct);
                var cidade = destino is null ? null : await BuscarCidadeAsync(destino, ct);
                motivo = destino is null ? "destino_nao_validado" : "municipio_nao_atendido";
                if (cidade is not null)
                {
                    if (cidade.TarifaCentavos <= long.MaxValue - subtotal)
                    {
                        AdicionarOferta(new(destino, cidade.Id), cidade.TarifaCentavos, cidade.Revisao);
                        motivo = null;
                    }
                    else
                        motivo = "limite_monetario_excedido";
                }
            }

            return new(ofertas, precos!, motivo);

            void AdicionarOferta(DadosEntregaPedido entrega, long frete, int? revisaoCidade = null)
            {
                var oferta = new OfertaProtegida(clienteId, carrinho, validade, configuracao.Revisao,
                    entrega.Destino is null ? null : enderecoCadastradoId, revisaoCidade, frete, entrega);
                var id = _ofertasEntrega!.Protect(JsonSerializer.Serialize(oferta));
                var modalidade = entrega.Destino is null
                    ? ModalidadeEntregaPedido.RetiradaLocal : ModalidadeEntregaPedido.EntregaPropria;
                ofertas.Add(new(id, modalidade.ToString(), validade, frete, checked(subtotal + frete), entrega));
            }
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return new([], [], "conflito_concorrencia");
        }
    }

    private async Task<(DadosEntregaPedido? Dados, long Frete, ModalidadeEntregaPedido Modalidade)?> ValidarEntregaAsync(
        CriarPedidoCompraInput input, DadosItemPedido[] itens, DateTimeOffset agora, CancellationToken ct)
    {
        var configuracao = await CarregarConfiguracaoAsync(ct);
        if (input.OfertaEntregaId is null)
            return configuracao.RetiradaHabilitada
                ? (configuracao.EnderecoRetirada is null && configuracao.InstrucoesRetirada is null
                    ? null : new DadosEntregaPedido(EnderecoRetirada: configuracao.EnderecoRetirada,
                        InstrucoesRetirada: configuracao.InstrucoesRetirada), 0, ModalidadeEntregaPedido.RetiradaLocal) : null;

        OfertaProtegida? oferta;
        try
        {
            var json = _ofertasEntrega?.Unprotect(input.OfertaEntregaId);
            oferta = json is null ? null : JsonSerializer.Deserialize<OfertaProtegida>(json);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }

        if (oferta is null || oferta.ClienteId != input.ClienteId || oferta.ValidaAteUtc <= agora
            || oferta.RevisaoConfiguracao != configuracao.Revisao || oferta.Carrinho != IdentificarCarrinho(itens))
            return null;

        if (oferta.Dados.Destino is not { } destino)
            return configuracao.RetiradaHabilitada ? (oferta.Dados, 0, ModalidadeEntregaPedido.RetiradaLocal) : null;

        if (!configuracao.EntregaPropriaHabilitada ||
            !_municipiosPorCep.TryGetValue(destino.Cep, out var ibge) || ibge != destino.CodigoIbge)
            return null;

        if (oferta.EnderecoCadastradoId.HasValue &&
            await ResolverDestinoAsync(input.ClienteId, oferta.EnderecoCadastradoId, null, ct) != destino)
            return null;

        var cidade = await BuscarCidadeAsync(destino, ct);
        return cidade is not null && cidade.Id == oferta.Dados.CidadeId && cidade.Revisao == oferta.RevisaoCidade
            && cidade.TarifaCentavos == oferta.Frete
            ? (oferta.Dados, oferta.Frete, ModalidadeEntregaPedido.EntregaPropria) : null;
    }

    private async Task<ConfiguracaoAtendimento> CarregarConfiguracaoAsync(CancellationToken ct) =>
        await _dbContext.Set<ConfiguracaoAtendimento>().AsNoTracking().SingleOrDefaultAsync(ct) ?? new();

    private async Task<CidadeEntregaPropria?> BuscarCidadeAsync(EnderecoEntregaPedido destino, CancellationToken ct)
    {
        var cidade = await _dbContext.Set<CidadeEntregaPropria>().AsNoTracking().SingleOrDefaultAsync(
            item => item.Ativa && item.CodigoIbge == destino.CodigoIbge, ct);
        return cidade is not null && cidade.Uf == destino.Uf &&
            string.Equals(cidade.Nome, destino.Cidade, StringComparison.InvariantCultureIgnoreCase) ? cidade : null;
    }

    private async Task<EnderecoEntregaPedido?> ResolverDestinoAsync(Guid clienteId, Guid? enderecoId,
        EnderecoCotacaoEntrega? novo, CancellationToken ct)
    {
        ClienteEndereco? endereco;
        if (novo is null)
        {
            if (!enderecoId.HasValue)
                return null;
            endereco = await _dbContext.ClienteEnderecos.AsNoTracking().SingleOrDefaultAsync(
                item => item.ClienteUserId == clienteId && item.Id == enderecoId, ct);
        }
        else
        {
            try
            {
                endereco = ClienteEndereco.Criar(Guid.NewGuid(), clienteId, novo.Cep, novo.Uf, novo.Cidade,
                    novo.Bairro, novo.Logradouro, novo.Numero, novo.Complemento, false);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        if (endereco is null || !_municipiosPorCep.TryGetValue(endereco.Cep, out var ibge))
            return null;
        return new(endereco.Cep, endereco.Uf, endereco.Cidade, endereco.Bairro, endereco.Logradouro,
            endereco.Numero, endereco.Complemento, ibge);
    }

    private static string IdentificarCarrinho(IEnumerable<DadosItemPedido> itens) =>
        HashOferta(JsonSerializer.Serialize(itens.Select(item => new
        {
            item.ProdutoId, item.ProdutoVarianteId, item.Quantidade, item.PrecoUnitarioCentavos,
            item.PrecoRegularCentavos, item.PromocaoId
        })))!;

    private static string? HashOferta(string? valor) =>
        valor is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
}
