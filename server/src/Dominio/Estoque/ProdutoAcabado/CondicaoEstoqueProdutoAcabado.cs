namespace Xaquiamuni.Dominio.Estoque;

public enum CondicaoEstoqueProdutoAcabado : short
{
    Vendavel = 1,
    Danificada = 2,
    Quarentena = 3
}

public enum TipoMovimentoProdutoAcabado : short
{
    EstoqueInicial = 1,
    EntradaManual = 2,
    SaidaManual = 3,
    Perda = 4,
    Descarte = 5,
    AjusteInventario = 6,
    TransferenciaCondicao = 7,
    Estorno = 8
}

public enum OrigemValidadeLoteProdutoAcabado : short
{
    PrazoPadrao = 1,
    InformadaManualmente = 2,
    SemControle = 3
}
