using System.Text;

namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Extractor for <c>text/plain</c>, <c>text/markdown</c>, <c>text/csv</c>.
/// Decodes bytes as UTF-8 (strict — rejects invalid sequences) with
/// optional BOM, then truncates at
/// <see cref="AttachmentConstraints.MaxDocumentTextCharacters"/>.
/// </summary>
public sealed class PlainTextExtractor : IAttachmentTextExtractor
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/csv",
    };

    /// <inheritdoc />
    public bool CanHandle(string mimeType) => mimeType is not null && Supported.Contains(mimeType);

    /// <inheritdoc />
    public ValueTask<string> ExtractAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        // Skip a UTF-8 BOM if present so it doesn't leak into the prompt.
        var span = content.AsSpan();
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        string text;
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            text = strict.GetString(span);
        }
        catch (DecoderFallbackException ex)
        {
            throw new AttachmentExtractionException(
                $"'{fileName}' is not valid UTF-8.", ex);
        }

        if (text.Length > AttachmentConstraints.MaxDocumentTextCharacters)
        {
            text = text[..AttachmentConstraints.MaxDocumentTextCharacters]
                   + $"\n\n[…truncated at {AttachmentConstraints.MaxDocumentTextCharacters} characters]";
        }

        return ValueTask.FromResult(text);
    }
}
