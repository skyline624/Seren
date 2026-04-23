namespace Seren.Application.Characters.Import;

/// <summary>
/// Canonical meter name for Character Card import telemetry. Declared
/// in Application (not Infrastructure) so the composition root can
/// subscribe OpenTelemetry to the meter without taking a dependency on
/// the concrete implementation.
/// </summary>
public static class CharacterImportMeter
{
    public const string Name = "seren.characters";
}

/// <summary>
/// Instrumentation hook for the Character Card import pipeline. Declared
/// in Application per DIP so the handler stays framework-agnostic ; the
/// concrete implementation (<c>OtelCharacterImportMetrics</c>) lives in
/// Infrastructure and talks to an OpenTelemetry <c>Meter</c>.
/// </summary>
/// <remarks>
/// One method per semantic event keeps the interface from becoming a
/// dumping ground. The handler calls <see cref="RecordImport"/> on every
/// terminal path (success + each <see cref="Seren.Contracts.Characters.CharacterImportError"/>
/// code) so dashboards can break down outcomes by provider, spec version
/// and whether an avatar was persisted.
/// </remarks>
public interface ICharacterImportMetrics
{
    /// <summary>
    /// Record the outcome of a card-import attempt.
    /// </summary>
    /// <param name="outcome">One of: <c>"ok"</c> or a
    /// <see cref="Seren.Contracts.Characters.CharacterImportError"/> code.</param>
    /// <param name="specVersion">Upstream spec string (<c>chara_card_v3</c> /
    /// <c>chara_card_v2</c>) — <c>"unknown"</c> when parsing failed before
    /// the spec field could be read.</param>
    /// <param name="hadAvatar">Whether the card included a PNG avatar.</param>
    /// <param name="elapsed">End-to-end handler duration.</param>
    void RecordImport(string outcome, string specVersion, bool hadAvatar, TimeSpan elapsed);
}
