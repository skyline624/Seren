using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>input:chat:abort</c> event sent by a client to
/// stop the active chat run upstream. When <see cref="RunId"/> is
/// omitted the hub aborts whatever run is currently active on the
/// session; passing it explicitly guards against races where a new
/// run has started between the user clicking Stop and the frame
/// arriving server-side.
/// </summary>
[ExportTsClass]
public sealed record ChatAbortPayload
{
    /// <summary>
    /// Specific run to abort. In Seren the <c>runId</c> equals the
    /// <c>clientMessageId</c> used on <c>input:text</c>, so the client
    /// already knows it without needing an additional server round-trip.
    /// </summary>
    public string? RunId { get; init; }
}
