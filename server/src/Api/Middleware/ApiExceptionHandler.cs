using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Xaquiamuni.Api.Middleware;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isConcurrencyConflict = IsConcurrencyConflict(exception);
        var statusCode = isConcurrencyConflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status500InternalServerError;
        var title = isConcurrencyConflict ? "Conflito de atualização" : "Erro interno";
        var detail = isConcurrencyConflict
            ? "O recurso foi alterado por outra operação. Atualize os dados e tente novamente."
            : "Não foi possível concluir a solicitação.";

        logger.LogError(exception, "Unhandled API request failure. StatusCode={StatusCode}", statusCode);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            if (current is SqlException { Number: 1205 or 1222 })
            {
                return true;
            }
        }

        return false;
    }
}
