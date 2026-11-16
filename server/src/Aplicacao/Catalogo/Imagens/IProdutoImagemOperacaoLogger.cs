namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public interface IProdutoImagemOperacaoLogger
{
    void RegistrarFalhaDeArmazenamento(
        Guid produtoId,
        Guid? imagemId,
        string? chave,
        string etapa,
        Exception? exception);
}
