namespace Seren.Contracts.Characters;

/// <summary>
/// Stable, machine-readable error codes surfaced by the Character Card
/// import pipeline. Exposed as plain <see cref="string"/> constants — the
/// TypeScript client consumes them as a union-typed field and maps each
/// value to an i18n key, which is simpler than marshalling a C# enum
/// across the JSON wire.
/// </summary>
/// <remarks>
/// These values are part of the REST contract. Never rename or repurpose
/// an existing code — add a new one if the taxonomy needs to grow.
/// </remarks>
public static class CharacterImportError
{
    /// <summary>Generic "we could not make sense of this file" — used when
    /// no more specific code applies (e.g. PNG without CCv3 tEXt chunks,
    /// empty name, missing required fields).</summary>
    public const string InvalidCard = "invalid_card";

    /// <summary>File declared a <c>spec</c> string we don't support (CCv1
    /// is the main culprit).</summary>
    public const string UnsupportedSpec = "unsupported_spec";

    /// <summary>Upload exceeded the hard byte cap (10 MB).</summary>
    public const string CardTooLarge = "card_too_large";

    /// <summary>Composed system prompt was empty after applying the
    /// CCv3 composition rules — card is technically valid but carries
    /// no persona content.</summary>
    public const string EmptyPrompt = "empty_prompt";

    /// <summary>PNG magic matched but the chunk sequence is malformed
    /// (invalid length, truncated IEND, corrupted tEXt).</summary>
    public const string MalformedPng = "malformed_png";

    /// <summary>JSON payload (either a .json file or the base64-decoded
    /// tEXt chunk) failed to parse.</summary>
    public const string MalformedJson = "malformed_json";
}
