namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Machine-readable codes for attachment validation failures. Mirrored on
/// the client via <c>AttachmentValidationErrorCode</c> in TypeScript so
/// localized UI messages can be picked from the error code without
/// parsing human strings.
/// </summary>
public static class AttachmentValidationError
{
    /// <summary>MIME type is not in the whitelist.</summary>
    public const string UnsupportedMime = "unsupported_mime";

    /// <summary>A single attachment exceeds <see cref="AttachmentConstraints.MaxPerAttachmentBytes"/>.</summary>
    public const string TooLarge = "too_large";

    /// <summary>The sum of attachment sizes exceeds <see cref="AttachmentConstraints.MaxTotalBytes"/>.</summary>
    public const string TotalTooLarge = "total_too_large";

    /// <summary>More than <see cref="AttachmentConstraints.MaxCount"/> attachments on a single message.</summary>
    public const string TooMany = "too_many";

    /// <summary>The base64 content failed to decode.</summary>
    public const string InvalidBase64 = "invalid_base64";

    /// <summary>The decoded bytes don't match the declared MIME type's magic signature.</summary>
    public const string MagicMismatch = "magic_mismatch";

    /// <summary>The client-declared <c>ByteSize</c> disagrees with the decoded length.</summary>
    public const string SizeMismatch = "size_mismatch";

    /// <summary>Filename is missing or blank.</summary>
    public const string InvalidFileName = "invalid_filename";
}
