using System.Globalization;
using System.Text;
using UglyToad.PdfPig;

namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Extractor for <c>application/pdf</c>. Uses
/// <see href="https://github.com/UglyToad/PdfPig">PdfPig</see> (MIT,
/// pure .NET — no native dep) to read page text. Caps at
/// <see cref="AttachmentConstraints.MaxPdfPages"/> pages and
/// <see cref="AttachmentConstraints.MaxDocumentTextCharacters"/>
/// characters to bound context-window impact on the LLM.
/// </summary>
/// <remarks>
/// PdfPig extracts <b>text only</b> — images embedded in a PDF (scan of a
/// paper document, diagram) are <i>not</i> seen by the LLM. Upgrading to
/// a rasterize-each-page path would rebuild the pipeline around OpenClaw's
/// image-attachment channel, which is a separate chantier.
/// </remarks>
public sealed class PdfTextExtractor : IAttachmentTextExtractor
{
    /// <inheritdoc />
    public bool CanHandle(string mimeType)
        => string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<string> ExtractAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = PdfDocument.Open(content);

            var builder = new StringBuilder();
            var pageCount = document.NumberOfPages;
            var pagesToProcess = Math.Min(pageCount, AttachmentConstraints.MaxPdfPages);

            for (var i = 1; i <= pagesToProcess; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(i);
                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append("\n\n");
                    }

                    builder.Append("[Page ").Append(i).Append("]\n");
                    builder.Append(pageText);
                }

                if (builder.Length > AttachmentConstraints.MaxDocumentTextCharacters)
                {
                    builder.Length = AttachmentConstraints.MaxDocumentTextCharacters;
                    builder.Append(
                        CultureInfo.InvariantCulture,
                        $"\n\n[…truncated at {AttachmentConstraints.MaxDocumentTextCharacters} characters]");
                    return ValueTask.FromResult(builder.ToString());
                }
            }

            if (pagesToProcess < pageCount)
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"\n\n[…{pageCount - pagesToProcess} further pages not processed (limit = {AttachmentConstraints.MaxPdfPages})]");
            }

            return ValueTask.FromResult(builder.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Catch general exception — PdfPig throws a variety of subclasses for malformed PDFs; we collapse them into a single typed exception for the caller.
        catch (Exception ex)
        {
            throw new AttachmentExtractionException(
                $"'{fileName}' could not be parsed as a PDF: {ex.Message}", ex);
        }
#pragma warning restore CA1031
    }
}
