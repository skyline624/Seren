namespace Seren.Domain.ValueObjects;

/// <summary>
/// Identifies a module (plugin instance) on the Seren bus.
/// Mirrors the AIRI <c>plugin-protocol</c> ModuleIdentity contract.
/// </summary>
/// <param name="Id">Unique instance identifier, e.g. <c>"telegram-01"</c>.</param>
/// <param name="PluginId">Plugin type identifier, e.g. <c>"telegram-bot"</c>. Shared across instances of the same plugin.</param>
/// <param name="Version">Optional plugin version string, e.g. <c>"0.1.0"</c>.</param>
/// <param name="Labels">Optional set of labels used for routing (e.g. <c>env=prod</c>).</param>
public sealed record ModuleIdentity(
    string Id,
    string PluginId,
    string? Version = null,
    IReadOnlyDictionary<string, string>? Labels = null);
