using System.Text.Json.Serialization;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// for the JSON-file character store. Using a source-gen context keeps
/// the serialization AOT-friendly and avoids reflection at runtime,
/// matching the pattern established by <c>SerenJsonContext</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<Character>))]
[JsonSerializable(typeof(Character))]
internal sealed partial class CharacterJsonContext : JsonSerializerContext;
