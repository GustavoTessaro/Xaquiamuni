using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public enum EtapaEntregaPostal : short { Postar = 1, CorrigirRastreamento, ConfirmarRecebimento }

public sealed record ComandoEntregaPostal(Guid PedidoId, Guid AdministradorId, Guid Chave,
    byte[] VersaoEsperada, EtapaEntregaPostal Etapa, string? Servico = null, string? Codigo = null);

public sealed record ResultadoEntregaPostal(int Status, string? Codigo = null,
    SituacaoPedido? Situacao = null, bool Repetida = false);

public interface IEntregaPostal
{
    Task<ResultadoEntregaPostal> ExecutarAsync(ComandoEntregaPostal comando, CancellationToken ct);
}
