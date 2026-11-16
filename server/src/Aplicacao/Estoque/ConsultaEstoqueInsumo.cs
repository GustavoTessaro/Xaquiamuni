using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public enum SituacaoEstoqueInsumo : byte
{
    SemEstoque = 1,
    AbaixoOuNoMinimo = 2,
    Normal = 3,
    MinimoNaoConfigurado = 4
}

public enum OrigemMovimentoInsumo : byte
{
    UnidadeFisica = 1,
    FormaCompra = 2,
    OperacaoManual = 3,
    ContagemFisica = 4,
    MovimentoOriginal = 5
}

public sealed record PaginacaoEstoqueInsumo(int Pagina = 1, int ItensPorPagina = 20)
{
    public const int TamanhoMaximoPagina = 100;

    public int CalcularSkip()
    {
        if (Pagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Pagina), "A página deve ser maior que zero.");
        }

        if (ItensPorPagina < 1 || ItensPorPagina > TamanhoMaximoPagina)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ItensPorPagina),
                $"O tamanho da página deve estar entre 1 e {TamanhoMaximoPagina}.");
        }

        try
        {
            return checked((Pagina - 1) * ItensPorPagina);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(Pagina), "A paginação excede o limite suportado.");
        }
    }
}

public sealed record ListarInsumosEstoqueFiltro(
    string? Nome = null,
    bool? Ativo = null,
    SituacaoEstoqueInsumo? Situacao = null,
    int Pagina = 1,
    int ItensPorPagina = 20);

public sealed record ListarMovimentosInsumoFiltro(
    Guid InsumoId,
    TipoMovimentoInsumo? Tipo = null,
    Guid? LoteId = null,
    int Pagina = 1,
    int ItensPorPagina = 20,
    Guid? MovimentoId = null);

public sealed record PaginaEstoque<T>(
    IReadOnlyList<T> Itens,
    int TotalItens,
    int Pagina,
    int ItensPorPagina);

public sealed record ResumoEstoqueInsumo(
    Guid InsumoId,
    string Nome,
    TipoInsumoEstoque Tipo,
    UnidadeFisicaEstoque UnidadeBase,
    bool Ativo,
    bool ControlaLote,
    bool ValidadeObrigatoria,
    decimal SaldoFisico,
    decimal SaldoUtilizavel,
    decimal SaldoReservado,
    decimal SaldoDisponivel,
    decimal? EstoqueMinimo,
    bool EstoqueMinimoConfigurado,
    SituacaoEstoqueInsumo Situacao);

public sealed record FormaCompraInsumoConsulta(
    Guid Id,
    string Nome,
    decimal QuantidadeEquivalentePorEmbalagem,
    UnidadeFisicaEstoque UnidadeBase,
    bool Ativa);

public sealed record PosicaoEstoqueInsumoConsulta(
    Guid? LoteId,
    string? IdentificacaoLote,
    string? Fornecedor,
    DateOnly? DataEntrada,
    DateOnly? DataValidade,
    bool? Bloqueado,
    bool Elegivel,
    bool TemRegistroSaldo,
    byte[]? Versao,
    decimal SaldoFisico,
    decimal SaldoUtilizavel,
    decimal SaldoReservado,
    decimal SaldoDisponivel);

public sealed record DetalheEstoqueInsumo(
    ResumoEstoqueInsumo Resumo,
    IReadOnlyList<FormaCompraInsumoConsulta> FormasCompra,
    IReadOnlyList<PosicaoEstoqueInsumoConsulta> Posicoes);

public sealed record MovimentoInsumoConsulta(
    Guid Id,
    TipoMovimentoInsumo Tipo,
    DirecaoMovimentoInsumo Direcao,
    decimal QuantidadeOriginal,
    UnidadeFisicaEstoque? UnidadeOriginal,
    decimal QuantidadeBase,
    UnidadeFisicaEstoque UnidadeBase,
    decimal SaldoAnterior,
    decimal SaldoPosterior,
    string Motivo,
    string? Observacao,
    OrigemMovimentoInsumo Origem,
    Guid ResponsavelId,
    DateTimeOffset RegistradoEmUtc,
    Guid? LoteId,
    string? IdentificacaoLote,
    Guid? FormaCompraId,
    string? FormaCompraNome,
    decimal? FatorCompra,
    UnidadeFisicaEstoque? UnidadeFatorCompra,
    Guid? MovimentoOriginalId);

public static class ClassificadorSituacaoEstoqueInsumo
{
    public static SituacaoEstoqueInsumo Classificar(decimal saldoDisponivel, decimal? estoqueMinimo)
    {
        if (saldoDisponivel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(saldoDisponivel), "O saldo disponível não pode ser negativo.");
        }

        if (estoqueMinimo is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(estoqueMinimo), "O estoque mínimo não pode ser negativo.");
        }

        if (saldoDisponivel == 0)
        {
            return SituacaoEstoqueInsumo.SemEstoque;
        }

        if (estoqueMinimo is null)
        {
            return SituacaoEstoqueInsumo.MinimoNaoConfigurado;
        }

        return saldoDisponivel <= estoqueMinimo.Value
            ? SituacaoEstoqueInsumo.AbaixoOuNoMinimo
            : SituacaoEstoqueInsumo.Normal;
    }
}
