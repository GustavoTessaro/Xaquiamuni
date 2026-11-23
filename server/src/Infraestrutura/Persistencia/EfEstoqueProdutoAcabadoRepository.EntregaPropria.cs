using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoEntregaPropria(Guid AdministradorId, Guid Chave, Guid PedidoId,
    byte[] VersaoEsperada, short Etapa, DateTimeOffset RealizadaEmUtc);

public sealed partial class EfEstoqueProdutoAcabadoRepository : IEntregaPropriaPedido
{
    async Task<ResultadoEntregaPropria> IEntregaPropriaPedido.ExecutarAsync(ComandoEntregaPropria comando, CancellationToken ct)
    {
        var iniciar = comando.Etapa == EtapaEntregaPropria.Iniciar;
        var esperado = iniciar ? SituacaoPedido.EmPreparacao : SituacaoPedido.EmEntrega;
        var destino = iniciar ? SituacaoPedido.EmEntrega : SituacaoPedido.Entregue;
        var operacoes = _dbContext.Set<OperacaoEntregaPropria>();
        try
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await AdquirirLockAsync($"local-delivery:{comando.AdministradorId:D}:{comando.Chave:D}", ct);
            var variantes = await BloquearPedidoEVariantesAsync(comando.PedidoId, ct);
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            if (!await AdministradorAptoAsync(comando.AdministradorId, agora, ct))
                return new(StatusEntregaPropria.AdministradorNaoAutorizado);

            var anterior = await operacoes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.AdministradorId == comando.AdministradorId && x.Chave == comando.Chave, ct);
            if (anterior is not null)
                return anterior.PedidoId == comando.PedidoId && anterior.Etapa == (short)comando.Etapa
                    && anterior.VersaoEsperada.AsSpan().SequenceEqual(comando.VersaoEsperada)
                    ? new(StatusEntregaPropria.Sucesso, destino, Repetida: true)
                    : new(StatusEntregaPropria.ChaveEmUso);

            var pedido = await _dbContext.Pedidos.Include(x => x.Itens)
                .SingleOrDefaultAsync(x => x.Id == comando.PedidoId, ct);
            if (pedido is null)
                return new(StatusEntregaPropria.PedidoNaoEncontrado);
            if (!pedido.Versao.AsSpan().SequenceEqual(comando.VersaoEsperada))
                return new(StatusEntregaPropria.VersaoDivergente);
            if (pedido.ModalidadeEntrega != ModalidadeEntregaPedido.EntregaPropria || pedido.Situacao != esperado)
                return new(StatusEntregaPropria.EstadoIncompativel);
            if (agora < (iniciar ? pedido.PreparacaoIniciadaEmUtc : pedido.SaidaEntregaEmUtc))
                return new(StatusEntregaPropria.EstadoIncompativel);

            if (iniciar)
            {
                var reservas = await CarregarReservasRetiradaAsync(pedido.Id, ct);
                if (!ConfirmarEntregaPedido.ReservasCorrespondem(pedido, reservas) ||
                    (await CarregarMovimentosRetiradaAsync(reservas, ct)).Count != 0)
                    return new(StatusEntregaPropria.ReservasInconsistentes);

                var baixa = await PrepararBaixasRetiradaAsync(reservas, variantes, comando.AdministradorId, ct,
                    motivo: "Saída para entrega própria");
                if (baixa.Status != ConsumirReservasStatus.Sucesso)
                    return new(baixa.Status switch
                    {
                        ConsumirReservasStatus.LoteInelegivel => StatusEntregaPropria.LoteInelegivel,
                        ConsumirReservasStatus.SaldoInsuficiente => StatusEntregaPropria.SaldoInsuficiente,
                        _ => StatusEntregaPropria.ReservasInconsistentes,
                    });
                agora = baixa.Movimentos[0].RegistradoEmUtc;
                await _dbContext.SaveChangesAsync(ct);
                pedido.IniciarEntregaPropria(comando.AdministradorId, agora);
            }
            else
            {
                pedido.ConfirmarRecebimentoEntregaPropria(comando.AdministradorId, agora);
            }

            operacoes.Add(new(comando.AdministradorId, comando.Chave, pedido.Id, comando.VersaoEsperada,
                (short)comando.Etapa, agora));
            await _dbContext.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return new(StatusEntregaPropria.Sucesso, destino);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(StatusEntregaPropria.ConflitoConcorrencia);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        {
            return new(StatusEntregaPropria.ConflitoConcorrencia);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(StatusEntregaPropria.ConflitoConcorrencia);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }
}
