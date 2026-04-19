using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Fired when OpenClaw reports that a pending approval (exec or plugin) has
/// been decided. The <see cref="Decision"/> field is the upstream literal
/// ("allow" / "deny") verbatim.
/// </summary>
public sealed record ApprovalResolvedNotification(
    string Id,
    string Kind,
    string Decision,
    string? ResolvedBy,
    long? ResolvedAtMs) : INotification;
