using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Seren.Application.Modules;

/// <summary>
/// Context passed to <see cref="ISerenModule.Configure"/>. Carries the host's
/// service collection, the application configuration, and the appsettings
/// section name pre-computed as <c>Modules:{ModuleId}</c> so modules don't
/// have to redeclare it.
/// </summary>
/// <remarks>
/// Keeping the section-name convention here (DRY) means every module uses
/// the same path pattern — operators get a predictable layout under
/// <c>Modules:</c> in <c>appsettings.json</c>.
/// </remarks>
public sealed record ModuleContext(
    IServiceCollection Services,
    IConfiguration Configuration,
    string SectionName);
