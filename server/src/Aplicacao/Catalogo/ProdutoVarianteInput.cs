namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed record ProdutoVarianteInput(
    string? Identificacao,
    string? UnidadeVenda,
    decimal? PrecoVenda,
    Guid? Id = null,
    decimal? PesoLiquidoGramas = null,
    decimal? VolumeLiquidoMililitros = null,
    IReadOnlyList<ProdutoVarianteCaracteristicaInput?>? Caracteristicas = null,
    bool? Ativa = null,
    bool? Padrao = null,
    Guid? NovoPadraoId = null,
    bool AtualizarMedidas = false);
