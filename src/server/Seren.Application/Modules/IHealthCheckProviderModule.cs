using Microsoft.Extensions.DependencyInjection;

namespace Seren.Application.Modules;

/// <summary>
/// Opt-in module capability: contributes health-check probes.
/// The host iterates every registered <see cref="ISerenModule"/> that also
/// implements this interface and calls <see cref="RegisterHealthChecks"/>
/// while the host's <c>IHealthChecksBuilder</c> is being assembled.
/// </summary>
public interface IHealthCheckProviderModule
{
    /// <summary>
    /// Registers the module's health-check probes onto the shared builder.
    /// Implementations should namespace their check names with the module
    /// id (e.g. <c>"audio:openai"</c>) to keep the global view readable.
    /// </summary>
    void RegisterHealthChecks(IHealthChecksBuilder builder);
}
