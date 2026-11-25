using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Pedidos;

public sealed record DadosPixPedido(TipoChavePixManual TipoChave, string ChavePix,
    string NomeRecebedor, string? InstrucoesAdicionais)
{
    public static DadosPixPedido From(ConfiguracaoPixManual config) =>
        new(config.TipoChave, config.ChavePix, config.NomeRecebedor, config.InstrucoesAdicionais);
}

public sealed record ConsultaPixPedido(long ValorCentavos, DateTimeOffset PrazoPagamentoEmUtc,
    bool PagamentoPermitido, DadosPixPedido? Dados, bool Legado);

public sealed record EstadoConfiguracaoPix(DadosPixPedido? Dados, long Revisao, int PedidosPendentes);

public enum StatusSalvarPix { Sucesso, RevisaoDesatualizada, ChaveIncompativel, ConfirmacaoPendente }

public sealed record ResultadoSalvarPix(StatusSalvarPix Status, EstadoConfiguracaoPix? Estado = null);

public interface IPagamentoPixManual
{
    Task<EstadoConfiguracaoPix> ConsultarConfiguracaoAsync(CancellationToken cancellationToken);
    Task<ResultadoSalvarPix> SalvarAsync(ConfiguracaoPixManual configuracao, long revisao, Guid autorId,
        Guid chave, bool confirmarPendentes, CancellationToken cancellationToken);
    Task<ConsultaPixPedido?> ConsultarPedidoAsync(Guid pedidoId, Guid clienteId, CancellationToken cancellationToken);
}
