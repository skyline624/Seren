namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Extracts plain text from a validated document attachment so the result
/// can be concatenated to the user message before calling OpenClaw
/// (OpenClaw's <c>chat.send</c> is text + images only — it does not parse
/// PDFs or documents itself).
/// </summary>
public interface IAttachmentTextExtractor
{
    /// <summary>
    /// Returns <c>true</c> when this extractor can handle
    /// <paramref name="mimeType"/>.
    /// </summary>
    bool CanHandle(string mimeType);

    /// <summary>
    /// Extracts text from <paramref name="content"/>. Implementations must
    /// respect <see cref="AttachmentConstraints.MaxDocumentTextCharacters"/>
    /// by truncating and appending a marker when the limit is reached.
    /// </summary>
    /// <param name="content">Raw document bytes (already base64-decoded and validated).</param>
    /// <param name="fileName">Original filename — used for diagnostic messages on parse failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted text. Never <c>null</c>; empty string allowed for documents with no text.</returns>
    ValueTask<string> ExtractAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when a <see cref="IAttachmentTextExtractor"/> fails to parse
/// a document (corrupt PDF, invalid UTF-8, …). The handler catches this
/// and surfaces a user-visible note in the extracted-text section rather
/// than failing the whole message.
/// </summary>
public sealed class AttachmentExtractionException : Exception
{
    public AttachmentExtractionException(string message) : base(message) { }
    public AttachmentExtractionException(string message, Exception innerException) : base(message, innerException) { }
    public AttachmentExtractionException() : base("Attachment text extraction failed.") { }
}
