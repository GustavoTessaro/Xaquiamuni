using System.Text.Json.Serialization;

namespace Xaquiamuni.Api.Pedidos;

public sealed record ConfirmarPagamentoAdminRequest(bool RecebimentoConferido);

public sealed record CancelarPedidoAdminRequest(string? Motivo);

public sealed record AvancarPreparacaoAdminRequest;

public sealed record ConfirmarEntregaAdminRequest(bool EntregaPresencialConfirmada);

public sealed record TransicaoEntregaPropriaRequest(bool Confirmado);
public sealed record RegistrarPostagemRequest(bool Confirmado, string? ServicoPostalUtilizado, string? CodigoRastreamento = null);
public sealed record CorrigirRastreamentoPostalRequest(string? CodigoRastreamento);
public sealed record ConfirmarEntregaPostalRequest(bool Confirmado);

public sealed record PedidoAdminResumoResponse(
    Guid Id, string Situacao, DateTimeOffset CriadoEmUtc, DateTimeOffset PrazoPagamentoEmUtc,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TotalCentavos, string NomeCliente, string NumeroPublico);

public sealed record PedidoAdminPaginaResponse(
    IReadOnlyList<PedidoAdminResumoResponse> Itens, int Pagina, int ItensPorPagina, int TotalItens, int TotalPaginas);

public sealed record PedidoAdminResponse(PedidoClienteResponse Pedido, ContatoPedidoAdminResponse Cliente);

public sealed record ContatoPedidoAdminResponse(string Nome, string? Email);

public sealed record ResultadoPedidoAdminResponse(Guid Id, string Situacao, bool Repetida);
