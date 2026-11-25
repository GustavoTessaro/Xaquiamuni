using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : ICriarPedidoCompraRepository
{
    async Task<CriarPedidoCompraResult> ICriarPedidoCompraRepository.CriarAsync(
        CriarPedidoCompraInput input, CancellationToken cancellationToken)
    {
        try
        {
            var oferta = LerOferta(input.OfertaEntregaId);
            var cep = oferta?.ClienteId == input.ClienteId && oferta.Dados.Destino is { } destino
                ? await _consultaCep.ConsultarAsync(destino.Cep, cancellationToken) : null;
            // Mant?m as leituras do cadastro e do cat?logo v?lidas at? o commit.
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            await AdquirirLockAsync($"purchase-request:{input.ClienteId:D}:{input.ChaveIdempotencia:D}", cancellationToken)
                .ConfigureAwait(false);

            var existente = await _dbContext.Pedidos.AsNoTracking().Include(pedido => pedido.Itens)
                .SingleOrDefaultAsync(pedido => pedido.ClienteId == input.ClienteId
                    && EF.Property<Guid?>(pedido, "ChaveIdempotencia") == input.ChaveIdempotencia,
                    cancellationToken).ConfigureAwait(false);
            if (existente is not null)
            {
                var mesmosItens = input.ConversaoCorreiosId is not null || existente.Itens.OrderBy(item => item.ProdutoVarianteId)
                    .Select(item => new ItemCompraInput(item.ProdutoId, item.ProdutoVarianteId, item.Quantidade,
                        item.PrecoAceitoCentavos))
                    .SequenceEqual(input.Itens);
                var mesmaModalidade = existente.CotacaoCorreiosId.HasValue == (input.ConversaoCorreiosId is not null);
                return mesmaModalidade && mesmosItens && existente.HashOfertaEntrega ==
                    HashOferta(input.ConversaoCorreiosId ?? input.OfertaEntregaId)
                    ? new(CriarPedidoCompraStatus.Sucesso, existente, Repetida: true)
                    : new(CriarPedidoCompraStatus.ConflitoIdempotencia);
            }

            var itens = input.Itens;
            ConversaoPostal? postal = null;
            CondicaoPostal? condicao = null;
            if (input.ConversaoCorreiosId is { } token)
            {
                condicao = LerCondicaoPostal(token);
                if (condicao is null) return new(CriarPedidoCompraStatus.ConversaoCorreiosInvalida);
                if (condicao.ClienteId != input.ClienteId)
                    return new(CriarPedidoCompraStatus.CotacaoCorreiosNaoEncontrada);
                await BloquearSolicitacaoPostalAsync(condicao.CotacaoId, cancellationToken);
                var (carregada, falha) = await CarregarConversaoPostalAsync(input.ClienteId, condicao.CotacaoId, cancellationToken);
                if (carregada is null) return new(falha);
                if (carregada.Proposta.Revisao != condicao.Revisao)
                    return new(CriarPedidoCompraStatus.ConversaoCorreiosInvalida);
                postal = carregada;
                itens = postal.ItensCompra();
            }
            else
            {
                await BloquearConfiguracaoEntregaAsync(cancellationToken);
            }
            if (_promocoesHabilitadas)
                await PromocoesComerciaisSql.BloquearAsync(_dbContext, escrita: false, cancellationToken);

            var pedidoId = Guid.NewGuid();
            await AdquirirLockPedidoAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            foreach (var item in itens)
            {
                await AdquirirLockVarianteAsync(item.ProdutoVarianteId, cancellationToken).ConfigureAwait(false);
            }

            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            if (!await ClienteAptoAsync(input.ClienteId, agora, cancellationToken).ConfigureAwait(false))
            {
                return new(CriarPedidoCompraStatus.ClienteInapto);
            }

            if (postal?.Proposta.ValidaAteUtc <= _timeProvider.GetUtcNow())
                return new(CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
            var indisponiveis = new List<Guid>();
            var (dados, precos) = await CarregarItensCompraAsync(itens, agora, cancellationToken,
                postal is null ? null : indisponiveis).ConfigureAwait(false);
            if (dados is null)
            {
                return new(CriarPedidoCompraStatus.ItemNaoCompravel, ItensIndisponiveis: postal is null ? null : indisponiveis);
            }
            if (postal is null && itens.Where((item, i) =>
                ((_promocoesHabilitadas && input.OfertaEntregaId is null) || item.PrecoAceitoCentavos.HasValue) &&
                item.PrecoAceitoCentavos != dados[i].PrecoUnitarioCentavos).Any())
                return new(CriarPedidoCompraStatus.PrecoAlterado, PrecosAtuais: precos);

            var entrega = postal is null
                ? await ValidarEntregaAsync(input, oferta, cep, dados, agora, cancellationToken)
                : (new DadosEntregaPedido(Destino: postal.Endereco, PropostaPostal: postal.Snapshot, DescontoFrete: postal.Proposta.DescontoFrete),
                    postal.Proposta.FreteCentavos, ModalidadeEntregaPedido.Correios);
            if (entrega is null)
                return new(CriarPedidoCompraStatus.OfertaEntregaInvalida);
            if (!TentarCalcularTotalCompra(dados, entrega.Value.Frete, out var subtotal, out var total))
                return new(CriarPedidoCompraStatus.LimiteMonetarioExcedido);

            var reservas = await PlanejarReservasCompraAsync(pedidoId, dados, agora,
                agora.Add(PrazoPagamentoCompra.Duracao), indisponiveis, cancellationToken);
            if (postal?.Proposta.ValidaAteUtc <= _timeProvider.GetUtcNow())
                return new(CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
            if (postal is not null && condicao!.Precos != IdentificarCarrinho(dados))
            {
                return new(CriarPedidoCompraStatus.PrecoAlterado, PrecosAtuais: precos, RevisaoPostal:
                    MontarRevisaoPostal(postal, dados, precos!, subtotal, total, indisponiveis));
            }
            if (indisponiveis.Count != 0)
                return new(CriarPedidoCompraStatus.SaldoInsuficiente, ItensIndisponiveis: postal is null ? null : indisponiveis);

            if (postal is not null)
                dados = dados.Select(x => x with { PrecoAceitoCentavos = x.PrecoUnitarioCentavos }).ToArray();

            var numeroPublico = await ReservarNumeroPublicoAsync(cancellationToken);
            if (numeroPublico is null)
                return new(CriarPedidoCompraStatus.ConflitoConcorrencia);

            Pedido pedido;
            try
            {
                pedido = Pedido.Criar(input.ClienteId, agora, dados, pedidoId, entrega.Value.Dados,
                    entrega.Value.Frete, entrega.Value.Modalidade, numeroPublico);
            }
            catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "itens")
            {
                return new(CriarPedidoCompraStatus.LimiteMonetarioExcedido);
            }

            var registro = _dbContext.Pedidos.Add(pedido);
            registro.Property<Guid?>("ChaveIdempotencia").CurrentValue = input.ChaveIdempotencia;
            registro.Property(entity => entity.HashOfertaEntrega).CurrentValue =
                HashOferta(input.ConversaoCorreiosId ?? input.OfertaEntregaId);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ReservasProdutoAcabado.AddRange(reservas);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (postal is not null)
            {
                if (postal.Proposta.ValidaAteUtc <= _timeProvider.GetUtcNow())
                    return new(CriarPedidoCompraStatus.CotacaoCorreiosExpirada);
                postal.Solicitacao.Situacao = SituacaoCotacaoCorreios.ConvertidaEmPedido;
                postal.Solicitacao.Versao = checked(postal.Solicitacao.Versao + 1);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(CriarPedidoCompraStatus.Sucesso, pedido);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return new(CriarPedidoCompraStatus.ConflitoConcorrencia);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CriarPedidoCompraStatus.ConflitoConcorrencia);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(CriarPedidoCompraStatus.ConflitoConcorrencia);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<string?> ReservarNumeroPublicoAsync(CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            var numero = NumeroPublicoPedido.Validar(_gerarNumeroPublico());
            var recurso = $"purchase-number:{numero}";
            var resultado = await _dbContext.Database.SqlQuery<int>($"""
                DECLARE @resultado int;
                EXEC @resultado = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction', @LockTimeout = 0;
                SELECT @resultado AS [Value];
                """).ToListAsync(ct);
            if (resultado.Single() >= 0 &&
                !await _dbContext.Pedidos.AnyAsync(x => x.NumeroPublico == numero, ct))
                return numero;
        }

        return null;
    }

    private async Task<List<ReservaProdutoAcabado>> PlanejarReservasCompraAsync(Guid pedidoId,
        IReadOnlyList<DadosItemPedido> dados, DateTimeOffset agora, DateTimeOffset? prazo,
        ICollection<Guid> indisponiveis, CancellationToken ct)
    {
        var reservas = new List<ReservaProdutoAcabado>();
        var data = _calendario.ObterDataEm(agora);
        foreach (var item in dados)
        {
            var alocacoes = await PlanejarAlocacoesAsync(new(pedidoId, item.ProdutoVarianteId, item.Quantidade),
                agora, prazo, data, ct);
            if (alocacoes.Count == 0) indisponiveis.Add(item.ProdutoVarianteId);
            else reservas.AddRange(alocacoes);
        }
        return reservas;
    }

    private Task<bool> ClienteAptoAsync(Guid clienteId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        (from perfil in _dbContext.ClientePerfis
         join usuario in _dbContext.Users on perfil.UserId equals usuario.Id
         join papelUsuario in _dbContext.UserRoles on usuario.Id equals papelUsuario.UserId
         join papel in _dbContext.Roles on papelUsuario.RoleId equals papel.Id
         where perfil.UserId == clienteId && usuario.EmailConfirmed && papel.NormalizedName == XaquiamuniRoles.Cliente
            && (!usuario.LockoutEnabled || usuario.LockoutEnd == null || usuario.LockoutEnd < agora)
         select perfil.UserId).AnyAsync(cancellationToken);

    private async Task<(DadosItemPedido[]? Dados, PrecoAtualPedido[]? Precos)> CarregarItensCompraAsync(
        IReadOnlyList<ItemCompraInput> itens, DateTimeOffset agora, CancellationToken cancellationToken,
        ICollection<Guid>? indisponiveis = null)
    {
        var variantes = itens.Select(item => item.ProdutoVarianteId).ToArray();
        var catalogo = await (
            from produto in EfCatalogoPublicoRepository.FiltrarProdutosVisiveis(_dbContext.Produtos)
            from variante in produto.Variantes
            where variante.Ativa && EF.Parameter(variantes).Contains(variante.Id)
            select new { produto.Id, produto.Nome, VarianteId = variante.Id, variante.Identificacao, variante.PrecoVenda }
        ).ToDictionaryAsync(item => item.VarianteId, cancellationToken).ConfigureAwait(false);
        var invalidos = itens.Where(item => !catalogo.TryGetValue(item.ProdutoVarianteId, out var encontrado)
            || encontrado.Id != item.ProdutoId).Select(item => item.ProdutoVarianteId).ToArray();
        if (invalidos.Length > 0)
        {
            if (indisponiveis is not null)
                foreach (var id in invalidos) indisponiveis.Add(id);
            return (null, null);
        }

        var resultado = new DadosItemPedido[itens.Count];
        var precos = new PrecoAtualPedido[itens.Count];
        var promocoes = _promocoesHabilitadas
            ? await PromocoesComerciaisSql.CarregarAsync(_dbContext, itens.Select(i => i.ProdutoId).Distinct().ToArray(),
                agora, cancellationToken) : null;
        for (var i = 0; i < itens.Count; i++)
        {
            var item = itens[i];
            var atual = catalogo[item.ProdutoVarianteId];

            var preco = PromocoesComerciaisSql.Resolver(promocoes?[atual.Id] ?? [], atual.VarianteId, atual.PrecoVenda, agora);
            precos[i] = new(atual.Id, atual.VarianteId, preco);
            resultado[i] = new DadosItemPedido(atual.Id, atual.VarianteId, atual.Nome, atual.Identificacao,
                item.Quantidade, preco.FinalCentavos, item.PrecoAceitoCentavos,
                preco.PromocaoId is null ? null : preco.RegularCentavos, preco.PromocaoId);
        }

        return (resultado, precos);
    }
}
