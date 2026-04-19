using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Fired when OpenClaw reports a new message added to a session (from an
/// external channel integration or from another operator). The infrastructure
/// router has already decoded the raw event payload into the fields below.
/// </summary>
/// <param name="SessionKey">OpenClaw session identifier.</param>
/// <param name="Role">Message author role ("user" / "assistant" / "system").</param>
/// <param name="Content">Text content of the message.</param>
/// <param name="Timestamp">Unix epoch milliseconds when the message was created upstream.</param>
/// <param name="Author">Optional display name of the external author.</param>
/// <param name="Channel">Optional source channel ("discord", "slack", …).</param>
/// <param name="Seq">Optional gateway sequence number.</param>
public sealed record SessionMessageReceivedNotification(
    string SessionKey,
    string Role,
    string Content,
    long? Timestamp,
    string? Author,
    string? Channel,
    long? Seq) : INotification;
