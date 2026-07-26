using FocusAI.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FocusAI.Api.Security;

/// <summary>
/// Maps application exceptions onto RFC 7807 problem documents. Anything not
/// recognised here becomes a generic 500 with no internal detail leaked.
/// </summary>
public sealed class AppExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, errors) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "FocusAI unhandled exception on {Path}", httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation(
                "FocusAI request rejected on {Path}: {Title}",
                httpContext.Request.Path,
                title);
        }

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status >= StatusCodes.Status500InternalServerError
                ? "Beklenmeyen bir hata oluştu."
                : exception.Message,
            Instance = httpContext.Request.Path
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int Status, string Title, IDictionary<string, string[]>? Errors) Map(Exception exception) =>
        exception switch
        {
            AppValidationException validation =>
                (StatusCodes.Status400BadRequest, "Doğrulama hatası", validation.Errors),
            NotFoundException => (StatusCodes.Status404NotFound, "Bulunamadı", null),
            ConflictException => (StatusCodes.Status409Conflict, "Çakışma", null),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Yetkisiz", null),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Erişim reddedildi", null),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "İstek iptal edildi", null),
            _ => (StatusCodes.Status500InternalServerError, "Sunucu hatası", null)
        };
}

internal static class StatusCodesExtensions
{
    /// <summary>Nginx's non-standard "client closed request"; ASP.NET has no constant for it.</summary>
    public const int Status499ClientClosedRequest = 499;
}
