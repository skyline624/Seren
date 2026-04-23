namespace Seren.Application.Characters.Import;

/// <summary>
/// Pure, I/O-free parser that turns raw Character Card bytes into a
/// normalised <see cref="CharacterCardData"/>. Single responsibility:
/// the mapping — no persistence, no broadcasts, no network calls.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must detect the payload kind from magic bytes (not
/// the <paramref name="fileName"/> extension, which is user-supplied
/// and untrustworthy) :
/// </para>
/// <list type="bullet">
/// <item><description>PNG signature <c>89 50 4E 47 0D 0A 1A 0A</c> →
/// walk the chunk sequence, decode the first <c>tEXt</c> / <c>zTXt</c>
/// keyed <c>ccv3</c> (v3) or <c>chara</c> (v2). APNG falls under the
/// same path — animation chunks are ignored.</description></item>
/// <item><description>Otherwise, assume JSON and parse directly.</description></item>
/// </list>
/// <para>
/// Parsing is strict: missing / empty required fields, unsupported
/// spec strings, corrupted PNG chunks, and over-budget payloads all
/// raise <see cref="CharacterImportException"/> with a stable
/// <see cref="Seren.Contracts.Characters.CharacterImportError"/> code.
/// </para>
/// <para>
/// The <paramref name="fileName"/> is passed only for log / error
/// enrichment — never used to open files or construct paths.
/// </para>
/// </remarks>
public interface ICharacterCardParser
{
    CharacterCardData Parse(ReadOnlyMemory<byte> bytes, string fileName);
}
