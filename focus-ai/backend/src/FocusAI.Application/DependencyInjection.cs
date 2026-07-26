using System.Reflection;
using FluentValidation;
using FocusAI.Application.Common.Behaviours;
using FocusAI.Application.Common.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FocusAI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters: exceptions wrap everything, validation runs before
            // the handler, and the timer measures the handler rather than itself.
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<IReaderContextFactory, ReaderContextFactory>();

        return services;
    }
}
