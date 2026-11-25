using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record SnapshotPixPedido(Guid PedidoId, long? Revisao, string? DadosJson)
{
    internal static SnapshotPixPedido Criar(Guid pedidoId, ConfiguracaoPixManual? configuracao) =>
        new(pedidoId, configuracao?.Revisao,
            configuracao is null ? null : JsonSerializer.Serialize(DadosPixPedido.From(configuracao)));
}

internal sealed record AlteracaoConfiguracaoPix(Guid AutorId, Guid Chave, string Hash,
    string ResultadoJson, DateTimeOffset RegistradaEmUtc);

internal sealed class EfPagamentoPixManual(XaquiamuniDbContext db) : IPagamentoPixManual
{
    public async Task<EstadoConfiguracaoPix> ConsultarConfiguracaoAsync(CancellationToken cancellationToken)
    {
        var config = await db.ConfiguracoesPixManual.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new(config is null ? null : DadosPixPedido.From(config), config?.Revisao ?? 0,
            await ContarPendentesAsync(cancellationToken));
    }

    internal static Task<ConfiguracaoPixManual?> LerParaPedidoAsync(XaquiamuniDbContext db, CancellationToken ct) =>
        db.ConfiguracoesPixManual.FromSqlRaw(
            "SELECT * FROM [ConfiguracoesPixManual] WITH (HOLDLOCK) WHERE [Id] = 1")
            .AsNoTracking().SingleOrDefaultAsync(ct);

    public async Task<ResultadoSalvarPix> SalvarAsync(ConfiguracaoPixManual configuracao, long revisao,
        Guid autorId, Guid chave, bool confirmarPendentes, CancellationToken cancellationToken)
    {
        var dados = DadosPixPedido.From(configuracao);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { dados, revisao, confirmarPendentes })));
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // O lock exclusivo precede a leitura de pedidos, na mesma ordem usada pelo checkout.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @resultado int;
            EXEC @resultado = sp_getapplock @Resource = N'configuracao-pix', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @resultado < 0 THROW 51000, 'Falha ao coordenar a configuração PIX.', 1;
            """, cancellationToken);
        var operacao = await db.Set<AlteracaoConfiguracaoPix>().FindAsync([autorId, chave], cancellationToken);
        if (operacao is not null)
            return operacao.Hash == hash
                ? new(StatusSalvarPix.Sucesso, JsonSerializer.Deserialize<EstadoConfiguracaoPix>(operacao.ResultadoJson))
                : new(StatusSalvarPix.ChaveIncompativel);

        var atual = await db.ConfiguracoesPixManual.SingleOrDefaultAsync(cancellationToken);
        if ((atual?.Revisao ?? 0) != revisao)
            return new(StatusSalvarPix.RevisaoDesatualizada);

        var pendentes = await ContarPendentesAsync(cancellationToken);
        if (pendentes > 0 && !confirmarPendentes && (atual is null || DadosPixPedido.From(atual) != dados))
            return new(StatusSalvarPix.ConfirmacaoPendente);

        configuracao.DefinirRevisao(checked(revisao + 1));
        if (atual is null)
            db.ConfiguracoesPixManual.Add(configuracao);
        else
            db.Entry(atual).CurrentValues.SetValues(configuracao);

        var resultado = new EstadoConfiguracaoPix(dados, configuracao.Revisao, pendentes);
        db.Add(new AlteracaoConfiguracaoPix(autorId, chave, hash, JsonSerializer.Serialize(resultado),
            await db.ObterAgoraReservasUtcAsync(cancellationToken)));
        await db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return new(StatusSalvarPix.Sucesso, resultado);
    }

    private Task<int> ContarPendentesAsync(CancellationToken ct) =>
        (from pedido in db.Pedidos
         from agora in db.Database.SqlQueryRaw<DateTimeOffset>(
             "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
         where pedido.Situacao == SituacaoPedido.AguardandoPagamento && pedido.PrazoPagamentoEmUtc > agora
         select pedido.Id).CountAsync(ct);

    public async Task<ConsultaPixPedido?> ConsultarPedidoAsync(
        Guid pedidoId, Guid clienteId, CancellationToken cancellationToken)
    {
        var consulta = await (
            from pedido in db.Pedidos.AsNoTracking()
            where pedido.Id == pedidoId && pedido.ClienteId == clienteId
            from agora in db.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
            let permitido = pedido.Situacao == SituacaoPedido.AguardandoPagamento && pedido.PrazoPagamentoEmUtc > agora
            from snapshot in db.Set<SnapshotPixPedido>().Where(s => permitido && s.PedidoId == pedido.Id).DefaultIfEmpty()
            from configuracao in db.ConfiguracoesPixManual.AsNoTracking().Where(_ => permitido && snapshot == null).DefaultIfEmpty()
            select new { pedido.TotalCentavos, pedido.PrazoPagamentoEmUtc, permitido, snapshot, configuracao }
        ).SingleOrDefaultAsync(cancellationToken);
        if (consulta is null) return null;
        var dados = consulta.snapshot?.DadosJson is { } json
            ? JsonSerializer.Deserialize<DadosPixPedido>(json)
            : consulta.configuracao is { } config ? DadosPixPedido.From(config) : null;
        return new(consulta.TotalCentavos, consulta.PrazoPagamentoEmUtc, consulta.permitido, dados,
            consulta.permitido && consulta.snapshot is null);
    }
}
