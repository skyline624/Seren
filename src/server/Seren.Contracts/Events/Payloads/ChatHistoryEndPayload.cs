using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:history:end</c> event marking the end of
/// a hydration or scroll-back batch. The client uses
/// <see cref="HasMore"/> to decide whether to keep offering scroll-back
/// to the user, and <see cref="OldestMessageId"/> as the cursor for the
/// next request.
/// </summary>
[ExportTsClass]
public sealed record ChatHistoryEndPayload
{
    /// <summary>True when more historical messages exist beyond what was just sent.</summary>
    public required bool HasMore { get; init; }

    /// <summary>
    /// Identifier of the oldest message included in this batch, suitable
    /// for the <c>before</c> field of the next request. <c>null</c> when
    /// the batch was empty.
    /// </summary>
    public string? OldestMessageId { get; init; }
}
