namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Thrown by <see cref="AttachmentValidator"/> when an inbound attachment
/// bundle fails validation. The <see cref="Code"/> property carries a
/// stable machine-readable identifier (see <see cref="AttachmentValidationError"/>)
/// so the WebSocket error frame can surface a localized message on the
/// client without parsing <see cref="Exception.Message"/>.
/// </summary>
public sealed class AttachmentValidationException : Exception
{
    /// <summary>Machine-readable error code from <see cref="AttachmentValidationError"/>.</summary>
    public string Code { get; }

    /// <summary>Optional human-readable context (filename, observed vs. declared size, …).</summary>
    public string? Details { get; }

    public AttachmentValidationException(string code, string message, string? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public AttachmentValidationException()
        : base("Attachment validation failed.")
    {
        Code = AttachmentValidationError.UnsupportedMime;
    }

    public AttachmentValidationException(string message)
        : base(message)
    {
        Code = AttachmentValidationError.UnsupportedMime;
    }

    public AttachmentValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = AttachmentValidationError.UnsupportedMime;
    }
}
