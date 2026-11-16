namespace Xaquiamuni.Aplicacao.Estoque;

public enum RegistrarEntradaInsumoStatus
{
    Sucesso,
    EntradaInvalida,
    InsumoNaoEncontrado,
    InsumoInativo,
    LoteNaoEncontrado,
    LoteInelegivel,
    ConflitoIdempotencia,
    ConflitoConcorrencia
}
