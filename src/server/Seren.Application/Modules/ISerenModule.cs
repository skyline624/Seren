using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Seren.Application.Modules;

/// <summary>
/// Compile-time module contract. A Seren module is a self-contained bundle
/// that brings a capability (audio, persona, weather, external integration…)
/// to the runtime via explicit DI registration. Modules are discovered by
/// type at startup — no runtime assembly scanning, no file-system manifest.
/// </summary>
/// <remarks>
/// <para>
/// The minimal contract is intentionally narrow (Interface Segregation
/// Principle): a module that only registers services declares only
/// <see cref="ISerenModule"/>. Modules that map endpoints, register health
/// checks, or expose other host capabilities additionally implement the
/// matching opt-in interface (<c>IEndpointMappingModule</c>,
/// <c>IHealthCheckProviderModule</c>, etc.).
/// </para>
/// <para>
/// Lifecycle is delegated to ASP.NET Core idioms — modules can register
/// <see cref="IHostedService"/> implementations in <see cref="Configure"/>
/// for setup/teardown semantics rather than reinventing init/dispose hooks.
/// </para>
/// </remarks>
public interface ISerenModule
{
    /// <summary>
    /// Stable, kebab-case identifier (e.g. <c>"audio"</c>, <c>"openclaw"</c>).
    /// Single source of truth: the appsettings section bound for this
    /// module's options is always <c>Modules:{Id}</c>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// SemVer string surfaced for diagnostics. Typically the assembly's
    /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Registers the module's services into the host's DI container.
    /// Called once at composition-root time, before <c>app.Build()</c>.
    /// Implementations should be idempotent and side-effect-free outside
    /// of the provided <see cref="ModuleContext.Services"/>.
    /// </summary>
    void Configure(ModuleContext context);
}
