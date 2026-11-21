using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record EnvioComprovantePix(Guid Id, Guid PedidoId, Guid ClienteId, Guid Chave,
    Guid? VersaoAnterior, DateTimeOffset EnviadoEmUtc, string Tipo, int TamanhoBytes, string Sha256)
{
    public ComprovantePixDto Dto() => new(Id, PedidoId, Id, EnviadoEmUtc, Tipo, TamanhoBytes);
}

internal sealed class ComprovantePixAtivo
{
    public Guid PedidoId { get; init; }
    public Guid EnvioId { get; set; }
    public byte[] Bytes { get; set; } = [];
    public EnvioComprovantePix Envio { get; set; } = null!;
}

public sealed partial class EfEstoqueProdutoAcabadoRepository : IComprovantesPix
{
    public Task<bool> PertenceAoClienteAsync(Guid pedido, Guid cliente, CancellationToken ct) =>
        _dbContext.Pedidos.AnyAsync(p => p.Id == pedido && p.ClienteId == cliente, ct);

    private IQueryable<ComprovantePixAtivo> Comprovante(Guid pedido, Guid? cliente) =>
        _dbContext.Set<ComprovantePixAtivo>().AsNoTracking().Where(c => c.PedidoId == pedido &&
            (cliente == null || _dbContext.Pedidos.Any(p => p.Id == pedido && p.ClienteId == cliente)));

    public Task<ComprovantePixDto?> ConsultarAsync(Guid pedido, Guid? cliente, CancellationToken ct) =>
        Comprovante(pedido, cliente).Select(c => new ComprovantePixDto(c.EnvioId, c.PedidoId,
            c.EnvioId, c.Envio.EnviadoEmUtc, c.Envio.Tipo, c.Envio.TamanhoBytes)).SingleOrDefaultAsync(ct);

    public Task<ConteudoComprovantePix?> BaixarAsync(Guid pedido, Guid? cliente, CancellationToken ct) =>
        Comprovante(pedido, cliente).Select(c => new ConteudoComprovantePix(c.Envio.Tipo, c.Bytes)).SingleOrDefaultAsync(ct);

    public async Task<ResultadoComprovantePix> EnviarAsync(Guid pedido, Guid cliente, Guid chave, Guid? versao,
        string tipo, byte[] bytes, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(ct);
        await AdquirirLockAsync($"pix-receipt:{cliente:D}:{chave:D}", ct);
        await AdquirirLockPedidoAsync(pedido, ct);
        var dados = await _dbContext.Pedidos.AsNoTracking().Where(p => p.Id == pedido && p.ClienteId == cliente)
            .Select(p => new { p.Situacao, p.PrazoPagamentoEmUtc }).SingleOrDefaultAsync(ct);
        if (dados is null) return new(StatusComprovantePix.Ausente);

        var envios = _dbContext.Set<EnvioComprovantePix>();
        var anterior = await envios.AsNoTracking().SingleOrDefaultAsync(e => e.ClienteId == cliente && e.Chave == chave, ct);
        if (anterior is not null)
        {
            return anterior.PedidoId == pedido && anterior.VersaoAnterior == versao && anterior.Sha256 == hash && anterior.Tipo == tipo
                ? new(StatusComprovantePix.Repetido, anterior.Dto())
                : new(StatusComprovantePix.ChaveDivergente);
        }

        var agora = await _dbContext.ObterAgoraReservasUtcAsync(ct);
        if (dados.Situacao != SituacaoPedido.AguardandoPagamento || dados.PrazoPagamentoEmUtc <= agora)
            return new(StatusComprovantePix.Inelegivel);

        var ativos = _dbContext.Set<ComprovantePixAtivo>();
        var ativo = await ativos.SingleOrDefaultAsync(c => c.PedidoId == pedido, ct);
        if (ativo?.EnvioId != versao) return new(StatusComprovantePix.VersaoDivergente);

        var envio = new EnvioComprovantePix(Guid.NewGuid(), pedido, cliente, chave, versao, agora, tipo, bytes.Length, hash);
        envios.Add(envio);
        if (ativo is null)
        {
            ativo = new() { PedidoId = pedido };
            ativos.Add(ativo);
        }

        ativo.Envio = envio;
        ativo.Bytes = bytes;
        await _dbContext.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new(StatusComprovantePix.Criado, envio.Dto());
    }
}
