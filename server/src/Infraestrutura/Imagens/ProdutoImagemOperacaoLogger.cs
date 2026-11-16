using Microsoft.Extensions.Logging;
using Xaquiamuni.Aplicacao.Catalogo.Imagens;

namespace Xaquiamuni.Infraestrutura.Imagens;

public sealed class ProdutoImagemOperacaoLogger(ILogger<ProdutoImagemOperacaoLogger> logger)
    : IProdutoImagemOperacaoLogger
{
    public void RegistrarFalhaDeArmazenamento(
        Guid produtoId,
        Guid? imagemId,
        string? chave,
        string etapa,
        Exception? exception) =>
        logger.LogError(
            exception,
            "Product image operation {Stage} needs operational follow-up for product {ProductId}, image {ImageId}, storage key {StorageKey}.",
            etapa,
            produtoId,
            imagemId,
            chave);
}
