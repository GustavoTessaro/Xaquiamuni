using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum EtapaEntregaPropria : short
{
    Iniciar = 1,
    ConfirmarRecebimento = 2,
}

public sealed record ComandoEntregaPropria(Guid PedidoId, Guid AdministradorId, Guid Chave,
    byte[] VersaoEsperada, EtapaEntregaPropria Etapa);

public enum StatusEntregaPropria
{
    Sucesso,
    AdministradorNaoAutorizado,
    PedidoNaoEncontrado,
    EstadoIncompativel,
    VersaoDivergente,
    ChaveEmUso,
    ConflitoConcorrencia,
    ReservasInconsistentes,
    LoteInelegivel,
    SaldoInsuficiente,
}

public sealed record ResultadoEntregaPropria(StatusEntregaPropria Status, SituacaoPedido? Situacao = null,
    bool Repetida = false);

public interface IEntregaPropriaPedido
{
    Task<ResultadoEntregaPropria> ExecutarAsync(ComandoEntregaPropria comando, CancellationToken ct);
}
