namespace Xaquiamuni.Aplicacao.Estoque;

public interface ICalendarioOperacionalEstoque
{
    DateOnly ObterDataAtual();
    DateOnly ObterDataEm(DateTimeOffset instanteUtc);
}
