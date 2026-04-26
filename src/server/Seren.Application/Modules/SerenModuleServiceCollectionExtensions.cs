using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Seren.Application.Modules;

/// <summary>
/// Composition-root extensions that wire <see cref="ISerenModule"/> instances
/// into the host's DI container. Module types are passed explicitly — there
/// is no runtime assembly scan or file-system manifest. Adding a module to
/// the host is a one-line code change in <c>Program.cs</c>.
/// </summary>
public static class SerenModuleServiceCollectionExtensions
{
    private const string SectionPrefix = "Modules";

    /// <summary>
    /// Registers each supplied module type. Each module is instantiated via
    /// its public parameterless constructor, registered as a singleton
    /// <see cref="ISerenModule"/> for downstream iteration (e.g. endpoint
    /// mapping), and its <see cref="ISerenModule.Configure"/> is invoked
    /// with a <see cref="ModuleContext"/> bound to <c>Modules:{Id}</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a supplied type does not implement <see cref="ISerenModule"/>
    /// or has no public parameterless constructor. Failing fast at startup
    /// avoids confusing runtime DI errors later.
    /// </exception>
    public static IServiceCollection AddSerenModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params Type[] moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(moduleTypes);

        foreach (var moduleType in moduleTypes)
        {
            ArgumentNullException.ThrowIfNull(moduleType);

            if (!typeof(ISerenModule).IsAssignableFrom(moduleType))
            {
                throw new ArgumentException(
                    $"Type '{moduleType.FullName}' does not implement {nameof(ISerenModule)}.",
                    nameof(moduleTypes));
            }

            var module = (ISerenModule?)Activator.CreateInstance(moduleType)
                ?? throw new ArgumentException(
                    $"Type '{moduleType.FullName}' could not be instantiated; "
                    + "modules must expose a public parameterless constructor.",
                    nameof(moduleTypes));

            // Each module is also surfaced as a concrete-type singleton so
            // host extensions (endpoint mapping, health checks) can resolve
            // capability-typed views without re-instantiating the module.
            services.AddSingleton(moduleType, module);
            services.AddSingleton<ISerenModule>(module);

            var sectionName = $"{SectionPrefix}:{module.Id}";
            module.Configure(new ModuleContext(services, configuration, sectionName));
        }

        return services;
    }
}

/// <summary>
/// Composition-root extensions that map module endpoints onto the host's
/// route table after <c>app.Build()</c>.
/// </summary>
public static class SerenModuleEndpointExtensions
{
    /// <summary>
    /// Iterates every registered <see cref="ISerenModule"/> that also
    /// implements <see cref="IEndpointMappingModule"/> and calls
    /// <see cref="IEndpointMappingModule.MapEndpoints"/>. Modules with no
    /// endpoints are skipped silently.
    /// </summary>
    public static IEndpointRouteBuilder MapSerenModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var modules = endpoints.ServiceProvider
            .GetServices<ISerenModule>()
            .OfType<IEndpointMappingModule>();

        foreach (var module in modules)
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
