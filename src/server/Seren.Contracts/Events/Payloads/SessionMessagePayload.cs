using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:session:message</c> event broadcast by the hub
/// when OpenClaw reports a new message added to a session (either by an
/// external channel like Discord/Slack or by another operator client).
/// </summary>
[ExportTsClass]
public sealed record SessionMessagePayload
{
    /// <summary>OpenClaw session identifier the message belongs to.</summary>
    public required string SessionKey { get; init; }

    /// <summary>Role of the message author (e.g. "user", "assistant", "system").</summary>
    public required string Role { get; init; }

    /// <summary>Text content of the message (may be empty for structured messages).</summary>
    public required string Content { get; init; }

    /// <summary>Unix epoch milliseconds when the message was created upstream.</summary>
    public long? Timestamp { get; init; }

    /// <summary>Optional display name of the external author (e.g. Discord username).</summary>
    public string? Author { get; init; }

    /// <summary>Optional source channel identifier ("discord", "slack", …) when the message came from an integration.</summary>
    public string? Channel { get; init; }

    /// <summary>Optional gateway sequence number for ordering consecutive messages within a run.</summary>
    public long? Seq { get; init; }
}
