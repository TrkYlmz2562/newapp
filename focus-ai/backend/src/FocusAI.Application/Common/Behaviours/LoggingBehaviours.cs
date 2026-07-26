using System.Diagnostics;
using FocusAI.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Common.Behaviours;

/// <summary>Structured entry log for every request, tagged with the caller.</summary>
public class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger,
    ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "FocusAI request {RequestName} by {UserId}",
            typeof(TRequest).Name,
            currentUser.UserId?.ToString() ?? "anonymous");

        return await next();
    }
}

/// <summary>
/// Warns when a handler runs long. The ingestion pipeline touches the network on
/// every step, so a slow handler is usually a misbehaving upstream rather than
/// a slow query — the log line needs to make that easy to spot.
/// </summary>
public class PerformanceBehaviour<TRequest, TResponse>(
    ILogger<PerformanceBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int WarnThresholdMs = 2000;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > WarnThresholdMs)
        {
            logger.LogWarning(
                "FocusAI long-running request {RequestName} took {ElapsedMs}ms",
                typeof(TRequest).Name,
                stopwatch.ElapsedMilliseconds);
        }

        return response;
    }
}

/// <summary>Last-resort logging so an unexpected failure is never silent.</summary>
public class UnhandledExceptionBehaviour<TRequest, TResponse>(
    ILogger<UnhandledExceptionBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FocusAI unhandled exception for {RequestName}", typeof(TRequest).Name);
            throw;
        }
    }
}
