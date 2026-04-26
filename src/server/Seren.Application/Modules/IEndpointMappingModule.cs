using Microsoft.AspNetCore.Routing;

namespace Seren.Application.Modules;

/// <summary>
/// Opt-in module capability: declares HTTP endpoints. The host iterates
/// every registered <see cref="ISerenModule"/> that also implements this
/// interface and calls <see cref="MapEndpoints"/> after the application
/// pipeline is built.
/// </summary>
/// <remarks>
/// Modules that don't expose endpoints simply don't implement this
/// interface (Interface Segregation Principle).
/// </remarks>
public interface IEndpointMappingModule
{
    /// <summary>
    /// Maps the module's minimal-API endpoints onto the host's route table.
    /// Called once after <c>app.Build()</c> from
    /// <c>SerenModuleEndpointExtensions.MapSerenModules</c>.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
