using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record ConsultaPixPedido(long ValorCentavos, DateTimeOffset PrazoPagamentoEmUtc,
    bool PagamentoPermitido, ConfiguracaoPixManual? Configuracao);

public interface IPagamentoPixManual
{
    Task<ConfiguracaoPixManual?> ConsultarConfiguracaoAsync(CancellationToken cancellationToken);
    Task SalvarAsync(ConfiguracaoPixManual configuracao, CancellationToken cancellationToken);
    Task<ConsultaPixPedido?> ConsultarPedidoAsync(Guid pedidoId, Guid clienteId, CancellationToken cancellationToken);
}
