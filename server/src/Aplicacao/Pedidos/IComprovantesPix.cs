namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ComprovantePixDto(Guid Id, Guid PedidoId, Guid Versao, DateTimeOffset EnviadoEmUtc,
    string Tipo, int TamanhoBytes);

public sealed record ConteudoComprovantePix(string Tipo, byte[] Bytes);

public enum StatusComprovantePix { Criado, Repetido, Ausente, Inelegivel, VersaoDivergente, ChaveDivergente }

public sealed record ResultadoComprovantePix(StatusComprovantePix Status, ComprovantePixDto? Comprovante = null);

public interface IComprovantesPix
{
    Task<bool> PertenceAoClienteAsync(Guid pedido, Guid cliente, CancellationToken ct);
    Task<ComprovantePixDto?> ConsultarAsync(Guid pedido, Guid? cliente, CancellationToken ct);
    Task<ConteudoComprovantePix?> BaixarAsync(Guid pedido, Guid? cliente, CancellationToken ct);
    Task<ResultadoComprovantePix> EnviarAsync(Guid pedido, Guid cliente, Guid chave, Guid? versao,
        string tipo, byte[] bytes, CancellationToken ct);
}
