using Xaquiamuni.Aplicacao.Catalogo.Imagens;

namespace Xaquiamuni.Api.Catalogo;

internal sealed class ProdutoImagemConteudoHttpResult(
    ProdutoImagemConteudoResult result,
    bool publicContent) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        using (result)
        {
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "image/webp";
            response.ContentLength = result.Imagem!.TamanhoBytes;
            response.Headers.CacheControl = publicContent ? "no-store" : "private, no-store";
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.ContentDisposition = "inline";

            await result.Conteudo!.CopyToAsync(
                    response.Body,
                    81920,
                    httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
    }
}
