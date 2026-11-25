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
        long Frete, DadosEntregaPedido Dados, int? RevisaoCampanha = null);

    private Task BloquearConfiguracaoEntregaAsync(CancellationToken ct) =>
        AdquirirLockAsync(EfConfiguracoesAtendimento.RecursoLock, ct, compartilhado: true);

    public async Task<CotacaoEntregaResult> CotarAsync(Guid clienteId, IReadOnlyList<ItemCompraInput> itens,
        Guid? enderecoCadastradoId, EnderecoCotacaoEntrega? endereco, bool usarEnderecoPadrao, CancellationToken ct)
    {
        if (_ofertasEntrega is null)
            return new([], [], "cotacao_indisponivel");

        try
        {
            var inicial = await LerEnderecoEntregaAsync(clienteId, enderecoCadastradoId, endereco, usarEnderecoPadrao, ct);
            var cep = inicial is null ? FalhaEndereco(endereco)
                : await _consultaCep.ConsultarAsync(inicial.Cep, ct);
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
                var atual = await LerEnderecoEntregaAsync(clienteId, enderecoCadastradoId, endereco, usarEnderecoPadrao, ct);
                if (usarEnderecoPadrao) enderecoCadastradoId = atual?.Id;
                var (destino, erro) = MontarDestino(atual, cep, exigeMunicipio: false);
                var cidade = destino is null ? null : await BuscarCidadeAsync(destino, ct);
                motivo = destino is null ? erro : "municipio_nao_atendido";
                if (cidade is not null)
                {
                    var (frete, desconto, revisaoCampanha) = await EfPromocoesFrete.CalcularEntregaPropriaAsync(
                        _dbContext, cidade.TarifaCentavos, subtotal, agora, ct);
                    if (frete <= long.MaxValue - subtotal)
                    {
                        AdicionarOferta(new(destino, cidade.Id, DescontoFrete: desconto), frete, cidade.Revisao, revisaoCampanha);
                        motivo = null;
                    }
                    else
                        motivo = "limite_monetario_excedido";
                }
            }

            return new(ofertas, precos!, motivo);

            void AdicionarOferta(DadosEntregaPedido entrega, long frete, int? revisaoCidade = null, int? revisaoCampanha = null)
            {
                var oferta = new OfertaProtegida(clienteId, carrinho, validade, configuracao.Revisao,
                    entrega.Destino is null ? null : enderecoCadastradoId, revisaoCidade, frete, entrega, revisaoCampanha);
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
        CriarPedidoCompraInput input, OfertaProtegida? oferta, ResultadoCep? cep, DadosItemPedido[] itens,
        DateTimeOffset agora, CancellationToken ct)
    {
        var configuracao = await CarregarConfiguracaoAsync(ct);
        if (input.OfertaEntregaId is null)
            return configuracao.RetiradaHabilitada
                ? (configuracao.EnderecoRetirada is null && configuracao.InstrucoesRetirada is null
                    ? null : new DadosEntregaPedido(EnderecoRetirada: configuracao.EnderecoRetirada,
                        InstrucoesRetirada: configuracao.InstrucoesRetirada), 0, ModalidadeEntregaPedido.RetiradaLocal) : null;

        if (oferta is null || oferta.ClienteId != input.ClienteId || oferta.ValidaAteUtc <= agora
            || oferta.RevisaoConfiguracao != configuracao.Revisao || oferta.Carrinho != IdentificarCarrinho(itens))
            return null;

        if (oferta.Dados.Destino is not { } destino)
            return configuracao.RetiradaHabilitada ? (oferta.Dados, 0, ModalidadeEntregaPedido.RetiradaLocal) : null;

        if (!configuracao.EntregaPropriaHabilitada || cep?.Endereco is not { } atual ||
            atual.Cep != destino.Cep || atual.CodigoIbge != destino.CodigoIbge ||
            (atual.Uf is not null && (atual.Uf != destino.Uf || !ResultadoCep.MesmoMunicipio(atual.Cidade, destino.Cidade))))
            return null;

        if (oferta.EnderecoCadastradoId.HasValue &&
            MontarDestino(await LerEnderecoEntregaAsync(input.ClienteId, oferta.EnderecoCadastradoId, null, false, ct),
                cep, exigeMunicipio: false).Destino != destino)
            return null;

        var cidade = await BuscarCidadeAsync(destino, ct);
        if (cidade is null || cidade.Id != oferta.Dados.CidadeId || cidade.Revisao != oferta.RevisaoCidade ||
            !TentarCalcularTotalCompra(itens, 0, out var subtotal, out _))
            return null;

        var (frete, desconto, revisaoCampanha) = await EfPromocoesFrete.CalcularEntregaPropriaAsync(
            _dbContext, cidade.TarifaCentavos, subtotal, agora, ct);
        return frete == oferta.Frete && desconto == oferta.Dados.DescontoFrete && revisaoCampanha == oferta.RevisaoCampanha
            ? (oferta.Dados, oferta.Frete, ModalidadeEntregaPedido.EntregaPropria) : null;
    }

    private OfertaProtegida? LerOferta(string? token)
    {
        if (token is null) return null;
        try
        {
            var json = _ofertasEntrega?.Unprotect(token);
            return json is null ? null : JsonSerializer.Deserialize<OfertaProtegida>(json);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private async Task<ConfiguracaoAtendimento> CarregarConfiguracaoAsync(CancellationToken ct) =>
        await _dbContext.Set<ConfiguracaoAtendimento>().AsNoTracking().SingleOrDefaultAsync(ct) ?? new();

    private async Task<CidadeEntregaPropria?> BuscarCidadeAsync(EnderecoEntregaPedido destino, CancellationToken ct)
    {
        var cidade = await _dbContext.Set<CidadeEntregaPropria>().AsNoTracking().SingleOrDefaultAsync(
            item => item.Ativa && item.CodigoIbge == destino.CodigoIbge, ct);
        return cidade is not null && cidade.Uf == destino.Uf &&
            ResultadoCep.MesmoMunicipio(cidade.Nome, destino.Cidade) ? cidade : null;
    }

    private async Task<ClienteEndereco?> LerEnderecoEntregaAsync(Guid clienteId, Guid? enderecoId,
        EnderecoCotacaoEntrega? novo, bool usarPadrao, CancellationToken ct)
    {
        if (novo is not null && !usarPadrao) return PrepararEndereco(clienteId, novo);
        if (!usarPadrao && !enderecoId.HasValue) return null;
        return await _dbContext.ClienteEnderecos.AsNoTracking().SingleOrDefaultAsync(
            item => item.ClienteUserId == clienteId && (usarPadrao ? item.IsDefault : item.Id == enderecoId), ct);
    }

    private static ClienteEndereco? PrepararEndereco(Guid clienteId, EnderecoCotacaoEntrega? novo)
    {
        if (novo is null) return null;
        try
        {
            return ClienteEndereco.Criar(Guid.NewGuid(), clienteId, novo.Cep, novo.Uf, novo.Cidade,
                novo.Bairro, novo.Logradouro, novo.Numero, novo.Complemento, false);
        }
        catch (ArgumentException) { return null; }
    }

    private static (EnderecoEntregaPedido? Destino, string? Erro) MontarDestino(
        ClienteEndereco? endereco, ResultadoCep consulta, bool exigeMunicipio)
    {
        if (endereco is null) return (null, consulta.Codigo ?? "endereco_incompleto");
        if (consulta.Endereco is not { } cep) return (null, consulta.Codigo);
        if (cep.Cep != endereco.Cep) return (null, "destino_nao_validado");
        if (cep.CodigoIbge is not { Length: 7 } || !cep.CodigoIbge.All(char.IsAsciiDigit))
            return (null, "cep_indisponivel");
        if (exigeMunicipio && cep.Cidade is null) return (null, "cep_nao_encontrado");
        if (cep.Uf is not null && (cep.Uf != endereco.Uf || !ResultadoCep.MesmoMunicipio(cep.Cidade, endereco.Cidade)))
            return (null, "cep_municipio_divergente");
        return (new(endereco.Cep, cep.Uf ?? endereco.Uf, cep.Cidade ?? endereco.Cidade,
            endereco.Bairro, endereco.Logradouro, endereco.Numero, endereco.Complemento, cep.CodigoIbge), null);
    }

    private static ResultadoCep FalhaEndereco(EnderecoCotacaoEntrega? endereco) =>
        new(null, !string.IsNullOrWhiteSpace(endereco?.Cep) && ResultadoCep.Normalizar(endereco.Cep) is null
            ? "cep_invalido" : "endereco_incompleto");

    private static string IdentificarCarrinho(IEnumerable<DadosItemPedido> itens) =>
        HashOferta(JsonSerializer.Serialize(itens.Select(item => new
        {
            item.ProdutoId, item.ProdutoVarianteId, item.Quantidade, item.PrecoUnitarioCentavos,
            item.PrecoRegularCentavos, item.PromocaoId
        })))!;

    private static string? HashOferta(string? valor) =>
        valor is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
}
