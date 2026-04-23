using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Characters;

/// <summary>
/// <see cref="IHostedService"/> that synchronises OpenClaw's workspace
/// persona files with Seren's currently-active character at boot.
/// </summary>
/// <remarks>
/// <para>
/// Seren and OpenClaw have independent lifecycles — if Seren restarts
/// while a Chub-imported character is active, the OpenClaw workspace
/// still reflects the persona from last time... unless a new
/// activation happens to occur. This hosted service closes that gap
/// by writing the active persona once at startup, so the state is
/// consistent before the first <c>chat.send</c>.
/// </para>
/// <para>
/// Best-effort by design : if the repository is empty (no active
/// character) or if the writer fails (bad permissions on the
/// workspace mount, etc.), the service logs and moves on — Seren's
/// startup is never blocked on persona sync.
/// </para>
/// </remarks>
public sealed class PersonaWorkspaceSynchronizer : IHostedService
{
    private readonly ICharacterRepository _repository;
    private readonly IPersonaWorkspaceWriter _writer;
    private readonly ILogger<PersonaWorkspaceSynchronizer> _logger;

    public PersonaWorkspaceSynchronizer(
        ICharacterRepository repository,
        IPersonaWorkspaceWriter writer,
        ILogger<PersonaWorkspaceSynchronizer> logger)
    {
        _repository = repository;
        _writer = writer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var active = await _repository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (active is null)
            {
                _logger.LogDebug("No active character at boot — workspace left untouched.");
                return;
            }
            await _writer.WritePersonaAsync(active, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Persona workspace synchronised at boot for active character {Character}.",
                active.Name);
        }
#pragma warning disable CA1031 // Startup synchronisation must never block the host from coming up.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persona workspace sync at boot failed — continuing anyway.");
        }
#pragma warning restore CA1031
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
