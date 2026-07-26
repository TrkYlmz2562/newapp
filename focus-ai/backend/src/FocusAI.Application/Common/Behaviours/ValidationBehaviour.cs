using FluentValidation;
using MediatR;
using AppValidationException = FocusAI.Application.Common.Exceptions.AppValidationException;

namespace FocusAI.Application.Common.Behaviours;

/// <summary>
/// Runs every FluentValidation validator registered for the request before the
/// handler sees it. Handlers therefore never re-check their own inputs.
/// </summary>
public class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (applicable.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            applicable.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length > 0)
        {
            var errors = failures
                .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
                .ToDictionary(g => g.Key, g => g.Distinct().ToArray());

            throw new AppValidationException(errors);
        }

        return await next();
    }
}
