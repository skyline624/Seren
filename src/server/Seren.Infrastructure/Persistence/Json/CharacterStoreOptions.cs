namespace Seren.Infrastructure.Persistence.Json;

/// <summary>
/// Options for <see cref="JsonCharacterRepository"/>. Bound from the
/// <c>Seren:Characters</c> section of <c>appsettings.json</c> or any
/// ASP.NET configuration provider (env vars, command-line, etc.).
/// </summary>
public sealed class CharacterStoreOptions
{
    public const string SectionName = "Seren:Characters";

    /// <summary>
    /// Absolute or relative path of the JSON file used as a flat-file
    /// character store. Defaults to a relative <c>characters.json</c>
    /// next to the running binary — production deployments should point
    /// this at a mounted volume (e.g. <c>/data/characters.json</c>).
    /// </summary>
    public string StorePath { get; set; } = "characters.json";
}
