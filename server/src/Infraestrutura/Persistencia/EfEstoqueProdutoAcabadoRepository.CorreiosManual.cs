using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : ICotacoesCorreios
{
    private const int LimiteCotacoesAbertas = 3;
    private const int LimiteCotacoesPorHora = 5;
    private readonly Dictionary<string, MunicipioPostal> _municipiosPostais;
    private static readonly JsonSerializerOptions JsonPostal = new(JsonSerializerDefaults.Web);
    private DbSet<SolicitacaoCorreios> SolicitacoesPostais => _dbContext.Set<SolicitacaoCorreios>();

    async Task<PaginaCotacoesCorreios> ICotacoesCorreios.ListarAsync(Guid? titular, FiltroCotacoesCorreios filtro, CancellationToken ct)
    {
        var agora = _timeProvider.GetUtcNow();
        var consulta = ConsultarSolicitacoesPostais(titular);
        if (filtro.CriadaDeUtc is { } de) consulta = consulta.Where(x => x.CriadaEmUtc >= de);
        if (filtro.CriadaAteUtc is { } ate) consulta = consulta.Where(x => x.CriadaEmUtc < ate);
        if (filtro.Situacao is { } situacao)
        {
            var propostas = new[] { SituacaoCotacaoCorreios.PropostaDisponivel, SituacaoCotacaoCorreios.Aceita };
            consulta = situacao == SituacaoCotacaoCorreios.Expirada
                ? consulta.Where(x => x.Situacao == situacao || propostas.Contains(x.Situacao) && x.ValidaAteUtc <= agora)
                : consulta.Where(x => x.Situacao == situacao && (!propostas.Contains(x.Situacao) || x.ValidaAteUtc > agora));
        }
        var total = await consulta.CountAsync(ct);
        var itens = await consulta.OrderByDescending(x => x.CriadaEmUtc).ThenBy(x => x.Id)
            .Skip((filtro.Pagina - 1) * filtro.ItensPorPagina).Take(filtro.ItensPorPagina).ToListAsync(ct);
        var ids = itens.Select(x => x.Id).ToArray();
        var vigentes = await (from proposta in _dbContext.Set<PropostaCorreios>().AsNoTracking()
            join solicitacao in SolicitacoesPostais.AsNoTracking() on proposta.SolicitacaoId equals solicitacao.Id
            where ids.Contains(proposta.SolicitacaoId) && proposta.Revisao == solicitacao.Revisao
            select proposta).ToDictionaryAsync(x => (x.SolicitacaoId, x.Revisao), ct);
        return new(itens.Select(x => ResumoPostal(x, agora,
            x.Revisao is { } revisao && vigentes.TryGetValue((x.Id, revisao), out var proposta) ? ValoresPostal(proposta) : null))
            .ToArray(), total, filtro.Pagina, filtro.ItensPorPagina);
    }

    async Task<DetalheCotacaoCorreios?> ICotacoesCorreios.BuscarAsync(Guid? titular, Guid id, CancellationToken ct)
    {
        var agora = _timeProvider.GetUtcNow();
        var cotacao = await ConsultarSolicitacoesPostais(titular).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (cotacao is null) return null;
        var propostas = await _dbContext.Set<PropostaCorreios>().AsNoTracking()
            .Where(x => x.SolicitacaoId == id && x.Revisao <= cotacao.Revisao).OrderByDescending(x => x.Revisao).ToListAsync(ct);
        Guid? pedidoConvertidoId = null;
        if (titular is { } clienteId && cotacao.Situacao == SituacaoCotacaoCorreios.ConvertidaEmPedido)
        {
            pedidoConvertidoId = await _dbContext.Pedidos.AsNoTracking()
                .Where(pedido => pedido.CotacaoCorreiosId == id && pedido.ClienteId == clienteId)
                .Select(pedido => (Guid?)pedido.Id)
                .SingleOrDefaultAsync(ct);
        }

        return new(ResumoPostal(cotacao, agora), cotacao.Versao, LerPostal<ItemCongeladoCorreios[]>(cotacao.ItensJson),
            LerPostal<EnderecoEntregaPedido>(cotacao.EnderecoJson), propostas.Select(x =>
                new PropostaCorreiosDto(x.Revisao, x.Servico, x.FreteCentavos, x.PrazoDias, x.ValidaAteUtc,
                    x.PesoTotalGramas, LerPostal<VolumeCorreios[]>(x.VolumesJson), x.PublicadaEmUtc, ValoresPostal(x))).ToArray(),
            cotacao.RevisaoAceita, pedidoConvertidoId);
    }

    async Task<ResultadoCotacaoCorreios> ICotacoesCorreios.ExecutarAsync(ComandoCotacaoCorreios comando, CancellationToken ct)
    {
        if (comando.Acao == AcaoCotacaoCorreios.Criar && comando.Admin ||
            comando.Acao == AcaoCotacaoCorreios.Publicar && !comando.Admin ||
            comando.Acao == AcaoCotacaoCorreios.Aceitar && comando.Admin)
            return new(403);
        try
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            if (!comando.Previa) await AdquirirLockAsync($"postal-key:{comando.Admin}:{comando.Autor:D}:{comando.Chave:D}", ct);
            var agora = _timeProvider.GetUtcNow();
            if (!(comando.Admin ? await AdministradorAptoAsync(comando.Autor, agora, ct) :
                await ClienteAptoAsync(comando.Autor, agora, ct)))
                return new(403);
            var operacoes = _dbContext.Set<OperacaoCotacaoCorreios>();
            // O fingerprint exclui o novo indicador de prévia para manter replays de operações antigas.
            var impressao = ImpressaoPostal(new
            {
                comando.Autor, comando.Admin, comando.Chave, comando.Id, comando.Acao, comando.Versao,
                comando.Solicitacao, comando.Proposta, comando.Decisao
            });
            var anterior = comando.Previa ? null : await operacoes.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Autor == comando.Autor && x.Admin == comando.Admin && x.Chave == comando.Chave, ct);
            if (anterior is not null)
                return anterior.Impressao == impressao ? LerPostal<ResultadoCotacaoCorreios>(anterior.ResultadoJson)
                    : new(409, "chave_em_uso");

            ResultadoCotacaoCorreios resultado;
            if (comando.Acao == AcaoCotacaoCorreios.Criar)
            {
                await AdquirirLockAsync($"postal-client:{comando.Autor:D}", ct);
                await BloquearConfiguracaoEntregaAsync(ct);
                agora = _timeProvider.GetUtcNow();
                resultado = await CriarSolicitacaoPostalAsync(comando, agora, ct);
            }
            else
            {
                await BloquearSolicitacaoPostalAsync(comando.Id!.Value, ct);
                if (comando.Acao == AcaoCotacaoCorreios.Publicar) await BloquearConfiguracaoEntregaAsync(ct);
                agora = _timeProvider.GetUtcNow();
                resultado = await AlterarSolicitacaoPostalAsync(comando, agora, ct);
            }

            if (comando.Previa || resultado.Cotacao is not { } cotacao) return resultado;
            operacoes.Add(new(comando.Autor, comando.Admin, comando.Chave, cotacao.Id, impressao, comando.Acao,
                agora, comando.Decisao?.Motivo?.Trim(), SerializarPostal(resultado)));
            await _dbContext.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(409, "conflito_concorrencia");
        }
        catch (SqlException e) when (e.Number is 1205 or 1222 or 51000)
        {
            return new(409, "conflito_concorrencia");
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(409, "conflito_concorrencia");
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<ResultadoCotacaoCorreios> CriarSolicitacaoPostalAsync(
        ComandoCotacaoCorreios comando, DateTimeOffset agora, CancellationToken ct)
    {
        if (!(await CarregarConfiguracaoAsync(ct)).CotacaoManualCorreiosHabilitada)
            return new(409, "cotacao_manual_desabilitada");
        var consulta = ConsultarSolicitacoesPostais(comando.Autor);
        if (await consulta.CountAsync(x => x.CriadaEmUtc > agora.AddHours(-1), ct) >= LimiteCotacoesPorHora)
            return new(429, "limite_frequencia");
        var solicitacao = comando.Solicitacao!;
        if (solicitacao.Itens is not { Length: > 0 and <= 100 } itens ||
            itens.Any(x => x is null || x.ProdutoId == Guid.Empty || x.ProdutoVarianteId == Guid.Empty || x.Quantidade <= 0)
            || itens.Select(x => x.ProdutoVarianteId).Distinct().Count() != itens.Length)
            return new(400, "itens_invalidos");
        var destino = ValidarEnderecoPostal(comando.Autor, solicitacao.Endereco);
        if (destino is null) return new(400, "destino_nao_validado");
        var ordenados = itens.OrderBy(x => x.ProdutoVarianteId).ToArray();
        var impressao = ImpressaoPostal(new { itens = ordenados, destino });
        var abertas = consulta.Where(x => x.Situacao == SituacaoCotacaoCorreios.AguardandoAnalise ||
            (x.Situacao == SituacaoCotacaoCorreios.PropostaDisponivel || x.Situacao == SituacaoCotacaoCorreios.Aceita)
            && x.ValidaAteUtc > agora);
        if (await abertas.AnyAsync(x => x.Impressao == impressao, ct)) return new(409, "solicitacao_duplicada");
        if (await abertas.CountAsync(ct) >= LimiteCotacoesAbertas) return new(429, "limite_solicitacoes_abertas");

        var ids = ordenados.Select(x => x.ProdutoVarianteId).ToArray();
        var catalogo = await (from produto in EfCatalogoPublicoRepository.FiltrarProdutosVisiveis(_dbContext.Produtos)
            from variante in produto.Variantes
            where variante.Ativa && EF.Parameter(ids).Contains(variante.Id)
            select new { produto.Id, produto.Nome, Variante = variante.Id, variante.Identificacao })
            .ToDictionaryAsync(x => x.Variante, ct);
        var congelados = new List<ItemCongeladoCorreios>(itens.Length);
        foreach (var item in ordenados)
        {
            if (!catalogo.TryGetValue(item.ProdutoVarianteId, out var dados) || dados.Id != item.ProdutoId)
                return new(400, "itens_invalidos");
            congelados.Add(new(item.ProdutoId, item.ProdutoVarianteId, item.Quantidade, dados.Nome, dados.Identificacao));
        }
        var nova = new SolicitacaoCorreios
        {
            Id = Guid.NewGuid(), ClienteId = comando.Autor, CriadaEmUtc = agora,
            ItensJson = SerializarPostal(congelados), EnderecoJson = SerializarPostal(destino), Impressao = impressao,
        };
        SolicitacoesPostais.Add(nova);
        return new(201, Cotacao: ResumoPostal(nova, agora), Versao: nova.Versao);
    }

    private async Task<ResultadoCotacaoCorreios> AlterarSolicitacaoPostalAsync(
        ComandoCotacaoCorreios comando, DateTimeOffset agora, CancellationToken ct)
    {
        var cotacao = await SolicitacoesPostais.SingleOrDefaultAsync(x => x.Id == comando.Id
            && (comando.Admin || x.ClienteId == comando.Autor), ct);
        if (cotacao is null) return new(404);
        if (cotacao.Versao != comando.Versao) return new(409, "versao_desatualizada");
        var situacao = cotacao.SituacaoEm(agora);
        if (situacao == SituacaoCotacaoCorreios.Expirada) return new(409, "proposta_expirada");
        var analise = situacao == SituacaoCotacaoCorreios.AguardandoAnalise;
        var disponivel = situacao == SituacaoCotacaoCorreios.PropostaDisponivel;
        var destino = comando.Acao switch
        {
            AcaoCotacaoCorreios.Publicar when analise || disponivel => SituacaoCotacaoCorreios.PropostaDisponivel,
            AcaoCotacaoCorreios.Aceitar when disponivel => SituacaoCotacaoCorreios.Aceita,
            AcaoCotacaoCorreios.Recusar when disponivel || (comando.Admin && analise) => SituacaoCotacaoCorreios.Recusada,
            AcaoCotacaoCorreios.Cancelar when analise || disponivel || situacao == SituacaoCotacaoCorreios.Aceita
                => SituacaoCotacaoCorreios.Cancelada,
            _ => situacao,
        };
        if (destino == situacao && comando.Acao != AcaoCotacaoCorreios.Publicar ||
            comando.Acao == AcaoCotacaoCorreios.Publicar && !analise && !disponivel)
            return new(409, "estado_incompativel");
        if (comando.Decisao?.Motivo?.Trim().Length > 300) return new(400, "motivo_invalido");
        if ((comando.Acao is AcaoCotacaoCorreios.Aceitar or AcaoCotacaoCorreios.Recusar) && disponivel
            && (!comando.Admin || comando.Decisao?.Revisao is not null) && comando.Decisao?.Revisao != cotacao.Revisao)
            return new(409, "revisao_desatualizada");
        ValoresPropostaCorreios? valores = null;
        if (comando.Acao == AcaoCotacaoCorreios.Publicar)
        {
            var configuracao = await CarregarConfiguracaoAsync(ct);
            var proposta = CriarPropostaPostal(comando, cotacao, agora, configuracao.ValidadePropostaCorreiosHoras);
            if (proposta is null) return new(400, "proposta_invalida");
            if (_promocoesHabilitadas)
                await PromocoesComerciaisSql.BloquearAsync(_dbContext, escrita: false, ct);
            var itens = LerPostal<ItemCongeladoCorreios[]>(cotacao.ItensJson).OrderBy(x => x.ProdutoVarianteId)
                .Select(x => new ItemCompraInput(x.ProdutoId, x.ProdutoVarianteId, x.Quantidade)).ToArray();
            foreach (var item in itens)
                await AdquirirLockVarianteAsync(item.ProdutoVarianteId, ct);
            var (dados, _) = await CarregarItensCompraAsync(itens, agora, ct);
            if (dados is null) return new(409, "itens_indisponiveis");
            if (!TentarCalcularTotalCompra(dados, 0, out var subtotal, out _))
                return new(400, "proposta_invalida");
            var (frete, desconto, _) = await EfPromocoesFrete.CalcularCorreiosAsync(
                _dbContext, proposta.FreteCentavos, subtotal, agora, ct);
            if (frete > long.MaxValue - subtotal) return new(400, "proposta_invalida");
            proposta = proposta with { FreteCentavos = frete, DescontoFrete = desconto, SubtotalProdutosCentavos = subtotal };
            valores = ValoresPostal(proposta);
            if (comando.Previa) return new(200, Cotacao: ResumoPostal(cotacao, agora, valores), Versao: cotacao.Versao);
            cotacao.Revisao = proposta.Revisao;
            cotacao.ValidaAteUtc = proposta.ValidaAteUtc;
            _dbContext.Set<PropostaCorreios>().Add(proposta);
        }
        if (destino == SituacaoCotacaoCorreios.Aceita) cotacao.RevisaoAceita = cotacao.Revisao;
        cotacao.Situacao = destino;
        cotacao.Versao = checked(cotacao.Versao + 1);
        return new(200, Cotacao: ResumoPostal(cotacao, agora, valores), Versao: cotacao.Versao);
    }

    private static PropostaCorreios? CriarPropostaPostal(ComandoCotacaoCorreios comando,
        SolicitacaoCorreios cotacao, DateTimeOffset agora, int validadeHoras)
    {
        var proposta = comando.Proposta!;
        var servico = proposta.Servico?.Trim();
        var validade = proposta.ValidaAteUtc ?? agora.AddHours(validadeHoras);
        var volumes = proposta.Volumes ?? [];
        if (servico is not { Length: > 0 and <= 60 } || proposta.FreteCentavos is not >= 0 ||
            proposta.PrazoDias is not > 0 || validade.Offset != TimeSpan.Zero || validade <= agora ||
            proposta.PesoTotalGramas is <= 0 || (proposta.PesoTotalGramas is null && volumes.Length == 0) ||
            volumes.Length > 20 || volumes.Any(x => x is null || x.PesoGramas <= 0 ||
                ((x.AlturaCm.HasValue || x.LarguraCm.HasValue || x.ComprimentoCm.HasValue) &&
                    !(x.AlturaCm > 0 && x.LarguraCm > 0 && x.ComprimentoCm > 0))) ||
            (proposta.PesoTotalGramas is { } peso && volumes.Length > 0 && volumes.Sum(x => (long)x.PesoGramas) != peso))
            return null;
        return new(cotacao.Id, checked((cotacao.Revisao ?? 0) + 1), servico, proposta.FreteCentavos.Value,
            proposta.PrazoDias.Value, validade, proposta.PesoTotalGramas, SerializarPostal(volumes), agora, comando.Autor);
    }

    private EnderecoEntregaPedido? ValidarEnderecoPostal(Guid titular, EnderecoCotacaoEntrega? input)
    {
        if (input is null) return null;
        try
        {
            var endereco = ClienteEndereco.Criar(Guid.NewGuid(), titular, input.Cep, input.Uf, input.Cidade,
                input.Bairro, input.Logradouro, input.Numero, input.Complemento, false);
            if (!_municipiosPostais.TryGetValue(endereco.Cep, out var municipio) ||
                municipio.CodigoIbge is not { Length: 7 } codigo || !codigo.All(char.IsAsciiDigit) ||
                municipio.Uf != endereco.Uf ||
                CultureInfo.InvariantCulture.CompareInfo.Compare(municipio.Cidade, endereco.Cidade,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) != 0)
                return null;
            return new(endereco.Cep, municipio.Uf, municipio.Cidade, endereco.Bairro, endereco.Logradouro,
                endereco.Numero, endereco.Complemento, codigo);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private IQueryable<SolicitacaoCorreios> ConsultarSolicitacoesPostais(Guid? titular) =>
        SolicitacoesPostais.AsNoTracking().Where(x => titular == null || x.ClienteId == titular);
    private Task BloquearSolicitacaoPostalAsync(Guid id, CancellationToken ct) =>
        AdquirirLockAsync($"postal-request:{id:D}", ct);
    private static ValoresPropostaCorreios ValoresPostal(PropostaCorreios proposta) =>
        new(proposta.FreteCentavos, proposta.SubtotalProdutosCentavos,
            proposta.SubtotalProdutosCentavos + proposta.FreteCentavos, proposta.DescontoFrete);

    private static ResumoCotacaoCorreios ResumoPostal(SolicitacaoCorreios x, DateTimeOffset agora,
        ValoresPropostaCorreios? valores = null) =>
        new(x.Id, x.SituacaoEm(agora).ToString(), x.CriadaEmUtc, x.Revisao, x.ValidaAteUtc, valores);
    private static string SerializarPostal<T>(T valor) => JsonSerializer.Serialize(valor, JsonPostal);
    private static T LerPostal<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonPostal)!;
    private static string ImpressaoPostal<T>(T valor) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(valor, JsonPostal)));
}
