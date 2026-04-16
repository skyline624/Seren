using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Behaviors;
using Seren.Application.Common;

namespace Seren.Application.DependencyInjection;

/// <summary>
/// DI extensions for the <c>Seren.Application</c> layer.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services: clock, validators, Mediator pipeline behaviors.
    /// Mediator itself must be registered separately in the composition root
    /// (<c>Seren.Server.Api</c>) because its source generator runs there.
    /// <para>
    /// Validators for <c>Sessions</c> and <c>Characters</c> are discovered
    /// automatically via <see cref="AssemblyMarker"/>. Mediator handlers are
    /// source-generated — no manual registration needed.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSerenApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock, SystemClock>();

        services.AddValidatorsFromAssemblyContaining<AssemblyMarker>(
            ServiceLifetime.Scoped,
            includeInternalTypes: false);

        return services;
    }
}
