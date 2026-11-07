using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;

namespace Playtrough.Api.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Do not pass the original exception to any logging provider. Messages,
        // Data and custom properties can contain credentials or complete email URLs.
        logger.LogError("Unhandled API error. TraceId: {TraceId}. Exception details: {ExceptionDetails}",
            context.TraceIdentifier, Describe(exception));
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Error interno", detail: "Ocurrió un error inesperado. Intentá nuevamente más tarde.",
            extensions: new Dictionary<string, object?> { ["code"] = "internal_error" }).ExecuteAsync(context);
        return true;
    }

    private static string Describe(Exception exception)
    {
        var result = new StringBuilder();
        Append(exception, result);
        return result.ToString();
    }

    private static void Append(Exception exception, StringBuilder result)
    {
        result.Append(exception.GetType().FullName).Append(" (HResult: ").Append(exception.HResult).AppendLine(")");
        result.AppendLine(new StackTrace(exception, false).ToString());
        if (exception is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions) Append(inner, result);
        else if (exception.InnerException is { } inner) Append(inner, result);
    }
}
