using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoEntregaPostal(Guid AdministradorId, Guid Chave, Guid PedidoId,
    string Impressao, EtapaEntregaPostal Etapa, DateTimeOffset RealizadaEmUtc, SituacaoPedido Resultado,
    string? CodigoAnterior, string? CodigoNovo);

public sealed partial class EfEstoqueProdutoAcabadoRepository : IEntregaPostal
{
    async Task<ResultadoEntregaPostal> IEntregaPostal.ExecutarAsync(ComandoEntregaPostal comando, CancellationToken ct)
    {
        try
        {
            var servico = comando.Servico?.Trim();
            var codigo = comando.Codigo is { } informado ? Pedido.NormalizarCodigoRastreamento(informado) : null;
            if (comando.Etapa == EtapaEntregaPostal.Postar && servico is not { Length: > 0 and <= 60 })
                return new(400, "servico_postal_invalido");
            if ((comando.Codigo is not null || comando.Etapa == EtapaEntregaPostal.CorrigirRastreamento) && codigo is null)
                return new(400, "codigo_rastreamento_invalido");
            var impressao = ImpressaoPostal(new { comando.PedidoId, comando.Etapa, comando.VersaoEsperada, servico, codigo });
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await AdquirirLockAsync($"postal-delivery:{comando.AdministradorId:D}:{comando.Chave:D}", ct);
            var variantes = await BloquearPedidoEVariantesAsync(comando.PedidoId, ct);
            var agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
            if (!await AdministradorAptoAsync(comando.AdministradorId, agora, ct)) return new(403);

            var operacoes = _dbContext.Set<OperacaoEntregaPostal>();
            var anterior = await operacoes.AsNoTracking().SingleOrDefaultAsync(x =>
                x.AdministradorId == comando.AdministradorId && x.Chave == comando.Chave, ct);
            if (anterior is not null)
                return anterior.Impressao == impressao
                    ? new(200, Situacao: anterior.Resultado, Repetida: true)
                    : new(409, "chave_em_uso");

            var pedido = await _dbContext.Pedidos.Include(x => x.Itens).SingleOrDefaultAsync(x => x.Id == comando.PedidoId, ct);
            if (pedido is null) return new(404);
            if (!pedido.Versao.AsSpan().SequenceEqual(comando.VersaoEsperada)) return new(409, "versao_desatualizada");
            if (pedido.ModalidadeEntrega != ModalidadeEntregaPedido.Correios) return new(409, "estado_incompativel");
            var codigoAnterior = pedido.CodigoRastreamento;
            switch (comando.Etapa)
            {
                case EtapaEntregaPostal.Postar:
                    if (pedido.Situacao != SituacaoPedido.EmPreparacao || pedido.PreparacaoIniciadaEmUtc > agora)
                        return new(409, "estado_incompativel");
                    var reservas = await CarregarReservasRetiradaAsync(pedido.Id, ct);
                    if (!ConfirmarEntregaPedido.ReservasCorrespondem(pedido, reservas) ||
                        (await CarregarMovimentosRetiradaAsync(reservas, ct)).Count != 0)
                        return new(409, "reservas_inconsistentes");
                    var baixa = await PrepararBaixasRetiradaAsync(reservas, variantes, comando.AdministradorId, ct,
                        motivo: "Saída para postagem postal");
                    if (baixa.Status != ConsumirReservasStatus.Sucesso)
                        return new(409, baixa.Status switch
                        {
                            ConsumirReservasStatus.LoteInelegivel => "lote_inelegivel",
                            ConsumirReservasStatus.SaldoInsuficiente => "saldo_insuficiente",
                            _ => "reservas_inconsistentes",
                        });
                    agora = baixa.Movimentos[0].RegistradoEmUtc;
                    await _dbContext.SaveChangesAsync(ct);
                    pedido.RegistrarPostagem(comando.AdministradorId, agora, servico!, codigo);
                    break;
                case EtapaEntregaPostal.CorrigirRastreamento:
                    if (pedido.Situacao is not (SituacaoPedido.Postado or SituacaoPedido.Entregue))
                        return new(409, "estado_incompativel");
                    if (codigo == codigoAnterior) return new(409, "codigo_inalterado");
                    pedido.CorrigirRastreamentoPostal(codigo!);
                    break;
                case EtapaEntregaPostal.ConfirmarRecebimento:
                    if (pedido.Situacao != SituacaoPedido.Postado || pedido.SaidaEntregaEmUtc > agora)
                        return new(409, "estado_incompativel");
                    pedido.ConfirmarEntregaPostal(comando.AdministradorId, agora);
                    break;
                default:
                    return new(400, "etapa_invalida");
            }

            operacoes.Add(new(comando.AdministradorId, comando.Chave, pedido.Id, impressao, comando.Etapa,
                agora, pedido.Situacao, codigoAnterior, pedido.CodigoRastreamento));
            await _dbContext.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return new(200, Situacao: pedido.Situacao);
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
}
