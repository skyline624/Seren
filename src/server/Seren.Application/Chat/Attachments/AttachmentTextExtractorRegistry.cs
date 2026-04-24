namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Resolves the right <see cref="IAttachmentTextExtractor"/> for a given
/// MIME type. Allows new document types to be supported by registering a
/// new extractor in DI — no switch to edit, no handler to change.
/// </summary>
public interface IAttachmentTextExtractorRegistry
{
    /// <summary>
    /// Returns the first extractor whose <see cref="IAttachmentTextExtractor.CanHandle"/>
    /// returns <c>true</c> for <paramref name="mimeType"/>, or <c>null</c>
    /// when none match (caller treats this as an extraction failure).
    /// </summary>
    IAttachmentTextExtractor? Resolve(string mimeType);
}

/// <summary>
/// Default registry — iterates the DI-injected set of extractors and
/// returns the first one whose <c>CanHandle</c> matches.
/// </summary>
public sealed class AttachmentTextExtractorRegistry : IAttachmentTextExtractorRegistry
{
    private readonly IReadOnlyList<IAttachmentTextExtractor> _extractors;

    public AttachmentTextExtractorRegistry(IEnumerable<IAttachmentTextExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        _extractors = [.. extractors];
    }

    /// <inheritdoc />
    public IAttachmentTextExtractor? Resolve(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        foreach (var extractor in _extractors)
        {
            if (extractor.CanHandle(mimeType))
            {
                return extractor;
            }
        }

        return null;
    }
}
