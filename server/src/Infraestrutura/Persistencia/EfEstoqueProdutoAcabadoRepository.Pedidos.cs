using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfEstoqueProdutoAcabadoRepository : ICriarPedidoCompraRepository
{
    async Task<CriarPedidoCompraResult> ICriarPedidoCompraRepository.CriarAsync(
        CriarPedidoCompraInput input, CancellationToken cancellationToken)
    {
        try
        {
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
                var mesmosItens = existente.Itens.OrderBy(item => item.ProdutoVarianteId)
                    .Select(item => new ItemCompraInput(item.ProdutoId, item.ProdutoVarianteId, item.Quantidade,
                        item.PrecoAceitoCentavos))
                    .SequenceEqual(input.Itens);
                return mesmosItens
                    ? new(CriarPedidoCompraStatus.Sucesso, existente, Repetida: true)
                    : new(CriarPedidoCompraStatus.ConflitoIdempotencia);
            }

            if (_promocoesHabilitadas)
                await PromocoesComerciaisSql.BloquearAsync(_dbContext, escrita: false, cancellationToken);

            var pedidoId = Guid.NewGuid();
            await AdquirirLockPedidoAsync(pedidoId, cancellationToken).ConfigureAwait(false);
            foreach (var item in input.Itens)
            {
                await AdquirirLockVarianteAsync(item.ProdutoVarianteId, cancellationToken).ConfigureAwait(false);
            }

            var agora = await _dbContext.ObterAgoraReservasUtcAsync(cancellationToken).ConfigureAwait(false);
            if (!await ClienteAptoAsync(input.ClienteId, agora, cancellationToken).ConfigureAwait(false))
            {
                return new(CriarPedidoCompraStatus.ClienteInapto);
            }

            var (dados, precos) = await CarregarItensCompraAsync(input.Itens, agora, cancellationToken).ConfigureAwait(false);
            if (dados is null)
            {
                return new(CriarPedidoCompraStatus.ItemNaoCompravel);
            }
            if (input.Itens.Where((item, i) =>
                (_promocoesHabilitadas || item.PrecoAceitoCentavos.HasValue) &&
                item.PrecoAceitoCentavos != dados[i].PrecoUnitarioCentavos).Any())
                return new(CriarPedidoCompraStatus.PrecoAlterado, PrecosAtuais: precos);

            Pedido pedido;
            try
            {
                pedido = Pedido.Criar(input.ClienteId, agora, dados, pedidoId);
            }
            catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "itens")
            {
                return new(CriarPedidoCompraStatus.LimiteMonetarioExcedido);
            }

            var reservas = new List<ReservaProdutoAcabado>();
            var dataOperacional = _calendario.ObterDataEm(agora);
            foreach (var item in pedido.Itens)
            {
                var alocacoes = await PlanejarAlocacoesAsync(
                    new(pedido.Id, item.ProdutoVarianteId, item.Quantidade),
                    agora, pedido.PrazoPagamentoEmUtc, dataOperacional, cancellationToken).ConfigureAwait(false);
                if (alocacoes.Count == 0)
                {
                    return new(CriarPedidoCompraStatus.SaldoInsuficiente);
                }

                reservas.AddRange(alocacoes);
            }

            _dbContext.Pedidos.Add(pedido).Property<Guid?>("ChaveIdempotencia").CurrentValue = input.ChaveIdempotencia;
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ReservasProdutoAcabado.AddRange(reservas);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(CriarPedidoCompraStatus.Sucesso, pedido);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
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

    private Task<bool> ClienteAptoAsync(Guid clienteId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        (from perfil in _dbContext.ClientePerfis
         join usuario in _dbContext.Users on perfil.UserId equals usuario.Id
         join papelUsuario in _dbContext.UserRoles on usuario.Id equals papelUsuario.UserId
         join papel in _dbContext.Roles on papelUsuario.RoleId equals papel.Id
         where perfil.UserId == clienteId && usuario.EmailConfirmed && papel.NormalizedName == XaquiamuniRoles.Cliente
            && (!usuario.LockoutEnabled || usuario.LockoutEnd == null || usuario.LockoutEnd < agora)
         select perfil.UserId).AnyAsync(cancellationToken);

    private async Task<(DadosItemPedido[]? Dados, PrecoAtualPedido[]? Precos)> CarregarItensCompraAsync(
        IReadOnlyList<ItemCompraInput> itens, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var variantes = itens.Select(item => item.ProdutoVarianteId).ToArray();
        var catalogo = await (
            from produto in EfCatalogoPublicoRepository.FiltrarProdutosVisiveis(_dbContext.Produtos)
            from variante in produto.Variantes
            where variante.Ativa && EF.Parameter(variantes).Contains(variante.Id)
            select new { produto.Id, produto.Nome, VarianteId = variante.Id, variante.Identificacao, variante.PrecoVenda }
        ).ToDictionaryAsync(item => item.VarianteId, cancellationToken).ConfigureAwait(false);

        var resultado = new DadosItemPedido[itens.Count];
        var precos = new PrecoAtualPedido[itens.Count];
        var promocoes = _promocoesHabilitadas
            ? await PromocoesComerciaisSql.CarregarAsync(_dbContext, itens.Select(i => i.ProdutoId).Distinct().ToArray(),
                agora, cancellationToken) : null;
        for (var i = 0; i < itens.Count; i++)
        {
            var item = itens[i];
            if (!catalogo.TryGetValue(item.ProdutoVarianteId, out var atual) || atual.Id != item.ProdutoId)
            {
                return (null, null);
            }

            var preco = PromocoesComerciaisSql.Resolver(promocoes?[atual.Id] ?? [], atual.VarianteId, atual.PrecoVenda, agora);
            precos[i] = new(atual.Id, atual.VarianteId, preco);
            resultado[i] = new DadosItemPedido(atual.Id, atual.VarianteId, atual.Nome, atual.Identificacao,
                item.Quantidade, preco.FinalCentavos, item.PrecoAceitoCentavos,
                preco.PromocaoId is null ? null : preco.RegularCentavos, preco.PromocaoId);
        }

        return (resultado, precos);
    }
}
