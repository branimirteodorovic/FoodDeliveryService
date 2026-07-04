using System.Reflection;
using FluentValidation;
using FoodDeliveryService.Common.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.Application;

/// <summary>
/// Application-layer bootstrap: registers MediatR (the CQRS in-process mediator) and
/// FluentValidation for the module assemblies hosted by an API host.
/// </summary>
public static class ApplicationConfiguration
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        Assembly[] moduleAssemblies)
    {
        // MediatR decouples endpoints from handlers: an endpoint calls ISender.Send(command/query)
        // and MediatR routes it to the single ICommandHandler/IQueryHandler registered for that
        // request type. All handlers in the module assemblies are discovered automatically.
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(moduleAssemblies);

            // Pipeline behaviors wrap every Send() like middleware, in this order:
            // 1. ExceptionHandling — converts unhandled exceptions into failure Results
            // 2. RequestLogging   — Serilog-logs each request with its outcome
            // 3. Validation       — runs the FluentValidation validators; short-circuits
            //    with a validation failure Result before the handler executes
            config.AddOpenBehavior(typeof(ExceptionHandlingPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        // FluentValidation: scans the module assemblies for AbstractValidator<TCommand>
        // implementations (they are internal, hence includeInternalTypes) and registers them
        // for the ValidationPipelineBehavior above.
        services.AddValidatorsFromAssemblies(moduleAssemblies, includeInternalTypes: true);

        return services;
    }
}
