using System.Text;
using System.Text.Json;
using Seren.Contracts.Characters;

namespace Seren.Application.Characters.Import;

/// <summary>
/// Default <see cref="ICharacterCardParser"/>. Supports the Character
/// Card v3 spec plus its v2 predecessor (identical data shape, different
/// <c>spec</c> string and — for PNG sources — different tEXt keyword).
/// </summary>
/// <remarks>
/// Pure function: takes a byte buffer + a logging-only filename, returns
/// a <see cref="CharacterCardData"/>. No I/O, no global state, safe to
/// register as a singleton. Hard 10 MB input cap as a last-line defense
/// behind the endpoint's <c>RequestSizeLimit</c>.
/// </remarks>
public sealed class CharacterCardV3Parser : ICharacterCardParser
{
    private const int MaxFileBytes = 10 * 1024 * 1024;
    private const int MaxSystemPromptChars = 4000;
    private const int MaxGreetingChars = 2000;
    private const int MaxDescriptionChars = 2000;
    private const int MaxTagsCount = 20;
    private const int MaxJsonDepth = 32;

    /// <summary>Warning code emitted when the composed prompt exceeds the
    /// domain validator's 4000-char limit and gets truncated.</summary>
    public const string WarningPromptTruncated = "prompt_truncated";

    /// <summary>Warning code emitted when <c>character_book</c> contains
    /// at least one keyword-activated (non-<c>constant</c>) entry — those
    /// are persisted in <see cref="CharacterCardData.ImportMetadataJson"/>
    /// but Seren does not yet consume them at chat time.</summary>
    public const string WarningLorebookDeferred = "lorebook_deferred";

    /// <summary>Warning code emitted when one or more of the long-text
    /// fields (greeting, description, extra tags) was truncated to fit
    /// Seren's storage bounds.</summary>
    public const string WarningFieldsTruncated = "fields_truncated";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = MaxJsonDepth,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    /// <inheritdoc />
    public CharacterCardData Parse(ReadOnlyMemory<byte> bytes, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (bytes.Length > MaxFileBytes)
        {
            throw new CharacterImportException(
                CharacterImportError.CardTooLarge,
                $"Card exceeds the {MaxFileBytes / (1024 * 1024)} MB cap.",
                details: $"bytes = {bytes.Length}, file = {fileName}");
        }

        byte[]? avatarPng = null;
        JsonDocument jsonDoc;

        if (PngTextChunkReader.IsPng(bytes.Span))
        {
            // Keep the full PNG as the 2D avatar; extract the embedded JSON.
            avatarPng = bytes.ToArray();
            jsonDoc = ExtractJsonFromPng(bytes, fileName);
        }
        else if (LooksLikeJson(bytes.Span))
        {
            jsonDoc = ParseJsonSafe(bytes);
        }
        else
        {
            throw new CharacterImportException(
                CharacterImportError.InvalidCard,
                "File is neither a PNG nor a JSON document.",
                details: $"file = {fileName}");
        }

        using (jsonDoc)
        {
            return Project(jsonDoc, avatarPng, fileName);
        }
    }

    // ── Dispatch helpers ────────────────────────────────────────────

    private static bool LooksLikeJson(ReadOnlySpan<byte> span)
    {
        // Skip UTF-8 BOM + ASCII whitespace, then expect an opening brace.
        for (var i = 0; i < Math.Min(32, span.Length); i++)
        {
            var b = span[i];
            if (b == (byte)'{')
            {
                return true;
            }
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF))
            {
                return false;
            }
        }
        return false;
    }

    private static JsonDocument ExtractJsonFromPng(ReadOnlyMemory<byte> bytes, string fileName)
    {
        string? base64V3 = null;
        string? base64V2 = null;

        foreach (var (keyword, text) in PngTextChunkReader.EnumerateTextChunks(bytes))
        {
            if (string.Equals(keyword, "ccv3", StringComparison.Ordinal))
            {
                base64V3 = text;
                break; // v3 wins over v2
            }
            if (base64V2 is null && string.Equals(keyword, "chara", StringComparison.Ordinal))
            {
                base64V2 = text;
            }
        }

        var base64Payload = base64V3 ?? base64V2
            ?? throw new CharacterImportException(
                CharacterImportError.InvalidCard,
                "PNG has no 'ccv3' or 'chara' tEXt chunk.",
                details: $"file = {fileName}");

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(base64Payload.Trim());
        }
        catch (FormatException ex)
        {
            throw new CharacterImportException(
                CharacterImportError.MalformedJson,
                "Character tEXt chunk is not valid base64.",
                details: ex.Message,
                inner: ex);
        }

        return ParseJsonSafe(decoded);
    }

    private static JsonDocument ParseJsonSafe(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            return JsonDocument.Parse(utf8Json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CharacterImportException(
                CharacterImportError.MalformedJson,
                "Card JSON is malformed.",
                details: ex.Message,
                inner: ex);
        }
    }

    // ── Projection (JSON → CharacterCardData) ───────────────────────

    private static CharacterCardData Project(JsonDocument doc, byte[]? avatarPng, string fileName)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new CharacterImportException(
                CharacterImportError.InvalidCard,
                "Card root is not a JSON object.",
                details: $"file = {fileName}");
        }

        var spec = GetString(root, "spec") ?? string.Empty;
        if (!IsSupportedSpec(spec))
        {
            throw new CharacterImportException(
                CharacterImportError.UnsupportedSpec,
                $"Unsupported card spec '{spec}'.",
                details: "supported: chara_card_v3, chara_card_v2");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new CharacterImportException(
                CharacterImportError.InvalidCard,
                "Card 'data' object is missing.");
        }

        var name = GetString(data, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CharacterImportException(
                CharacterImportError.InvalidCard,
                "Card 'data.name' is missing or empty.");
        }

        var warnings = new List<string>();
        var systemPrompt = ComposeSystemPrompt(data, name);
        if (systemPrompt.Length == 0)
        {
            throw new CharacterImportException(
                CharacterImportError.EmptyPrompt,
                "Composed system prompt is empty — card carries no persona content.");
        }
        if (systemPrompt.Length > MaxSystemPromptChars)
        {
            systemPrompt = systemPrompt[..MaxSystemPromptChars];
            warnings.Add(WarningPromptTruncated);
        }

        var greetingRaw = GetString(data, "first_mes");
        var greeting = greetingRaw is null ? null : SubstituteMacros(greetingRaw, name);
        var description = GetString(data, "description");
        var tags = GetStringArray(data, "tags");
        var creator = GetString(data, "creator");
        var characterVersion = GetString(data, "character_version");

        // Apply storage bounds — truncate over-long fields and mark the
        // warning rather than rejecting the card. Losing the tail of a
        // long greeting is a better UX than rejecting an otherwise valid
        // import.
        var fieldsTruncated = false;
        if (greeting is not null && greeting.Length > MaxGreetingChars)
        {
            greeting = greeting[..MaxGreetingChars];
            fieldsTruncated = true;
        }
        if (description is not null && description.Length > MaxDescriptionChars)
        {
            description = description[..MaxDescriptionChars];
            fieldsTruncated = true;
        }
        if (tags.Count > MaxTagsCount)
        {
            tags = [.. tags.Take(MaxTagsCount)];
            fieldsTruncated = true;
        }
        if (fieldsTruncated)
        {
            warnings.Add(WarningFieldsTruncated);
        }

        var metadataJson = BuildImportMetadataJson(data, out var hasUnusedLorebook);
        if (hasUnusedLorebook)
        {
            warnings.Add(WarningLorebookDeferred);
        }

        return new CharacterCardData(
            SpecVersion: spec,
            Name: name,
            SystemPrompt: systemPrompt,
            Greeting: greeting,
            Description: description,
            Tags: tags,
            Creator: creator,
            CharacterVersion: characterVersion,
            AvatarPng: avatarPng,
            ImportMetadataJson: metadataJson,
            Warnings: warnings);
    }

    private static bool IsSupportedSpec(string spec)
        => spec is "chara_card_v3" or "chara_card_v2";

    /// <summary>
    /// Composes the system prompt per CCv3 semantics :
    /// <list type="number">
    /// <item><description>Use <c>data.system_prompt</c> when non-empty.</description></item>
    /// <item><description>Otherwise concat <c>description / personality / scenario</c>
    /// separated by blank lines.</description></item>
    /// <item><description>Append <c>character_book.entries</c> flagged
    /// <c>constant: true &amp;&amp; enabled: true</c>, sorted by
    /// <c>insertion_order</c> ASC.</description></item>
    /// <item><description>Substitute <c>{{char}}</c> →
    /// <paramref name="characterName"/> and <c>{{user}}</c> → literal
    /// <c>"user"</c>.</description></item>
    /// </list>
    /// </summary>
    private static string ComposeSystemPrompt(JsonElement data, string characterName)
    {
        var explicitPrompt = GetString(data, "system_prompt");

        string baseText;
        if (!string.IsNullOrWhiteSpace(explicitPrompt))
        {
            baseText = explicitPrompt;
        }
        else
        {
            var parts = new[]
            {
                GetString(data, "description"),
                GetString(data, "personality"),
                GetString(data, "scenario"),
            }.Where(s => !string.IsNullOrWhiteSpace(s));
            baseText = string.Join("\n\n", parts);
        }

        var constants = CollectConstantLorebookEntries(data);
        if (constants.Count > 0)
        {
            var ordered = string.Join("\n\n", constants);
            baseText = string.IsNullOrWhiteSpace(baseText) ? ordered : $"{baseText}\n\n{ordered}";
        }

        return SubstituteMacros(baseText.Trim(), characterName);
    }

    private static List<string> CollectConstantLorebookEntries(JsonElement data)
    {
        var results = new List<(int Order, string Content)>();

        if (!data.TryGetProperty("character_book", out var book)
            || book.ValueKind != JsonValueKind.Object
            || !book.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // enabled defaults to true when absent (CCv3 spec).
            var enabled = !entry.TryGetProperty("enabled", out var e) || e.ValueKind != JsonValueKind.False;
            var constant = entry.TryGetProperty("constant", out var c) && c.ValueKind == JsonValueKind.True;
            if (!enabled || !constant)
            {
                continue;
            }

            var content = GetString(entry, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var order = 0;
            if (entry.TryGetProperty("insertion_order", out var o)
                && o.ValueKind == JsonValueKind.Number
                && o.TryGetInt32(out var parsed))
            {
                order = parsed;
            }

            results.Add((order, content));
        }

        return [.. results.OrderBy(r => r.Order).Select(r => r.Content)];
    }

    /// <summary>
    /// Replace the CCv3 macro tokens. <c>{{char}}</c> → <paramref name="characterName"/>,
    /// <c>{{user}}</c> → literal <c>"user"</c> because Seren has no
    /// per-user persona concept today — leaving the raw macro in the
    /// prompt would confuse the LLM.
    /// </summary>
    private static string SubstituteMacros(string text, string characterName)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        return text
            .Replace("{{char}}", characterName, StringComparison.Ordinal)
            .Replace("{{user}}", "user", StringComparison.Ordinal);
    }

    private static string? GetString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static List<string> GetStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    list.Add(s);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Build the opaque metadata JSON blob that preserves CCv3 fields
    /// Seren does not yet interpret (alternate_greetings, character_book,
    /// mes_example, post_history_instructions, creator_notes, source,
    /// extensions). Also reports whether <c>character_book</c> contains
    /// at least one keyword-activated (non-constant) entry so the UI
    /// can surface a "lorebook stored but not yet used" notice.
    /// </summary>
    private static string BuildImportMetadataJson(JsonElement data, out bool hasUnusedLorebookEntries)
    {
        hasUnusedLorebookEntries = DetectUnusedLorebookEntries(data);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            CopyIfPresent(writer, data, "alternate_greetings");
            CopyIfPresent(writer, data, "mes_example");
            CopyIfPresent(writer, data, "post_history_instructions");
            CopyIfPresent(writer, data, "creator_notes");
            CopyIfPresent(writer, data, "source");
            CopyIfPresent(writer, data, "extensions");
            CopyIfPresent(writer, data, "character_book");
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool DetectUnusedLorebookEntries(JsonElement data)
    {
        if (!data.TryGetProperty("character_book", out var book)
            || book.ValueKind != JsonValueKind.Object
            || !book.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var constant = entry.TryGetProperty("constant", out var c) && c.ValueKind == JsonValueKind.True;
            if (!constant)
            {
                return true;
            }
        }
        return false;
    }

    private static void CopyIfPresent(Utf8JsonWriter writer, JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out var value))
        {
            writer.WritePropertyName(name);
            value.WriteTo(writer);
        }
    }
}
