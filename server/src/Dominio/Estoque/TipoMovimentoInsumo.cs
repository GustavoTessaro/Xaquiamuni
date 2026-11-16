namespace Xaquiamuni.Dominio.Estoque;

public enum TipoMovimentoInsumo : short
{
    EstoqueInicial = 1,
    Aquisicao = 2,
    SaidaManual = 3,
    Perda = 4,
    Descarte = 5,
    AjusteInventario = 6,
    Estorno = 7
}

public enum DirecaoMovimentoInsumo : short
{
    Entrada = 1,
    Saida = -1
}
