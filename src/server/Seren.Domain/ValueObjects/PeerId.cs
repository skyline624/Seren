namespace Seren.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for a connected peer (UI client, plugin, external module).
/// </summary>
public readonly record struct PeerId(string Value)
{
    /// <summary>
    /// Creates a new random <see cref="PeerId"/> backed by a compact GUID (32 hex chars).
    /// </summary>
    public static PeerId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> is a non-empty string.
    /// </summary>
    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);

    public override string ToString() => Value;
}
