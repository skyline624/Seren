using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Fired when OpenClaw asks an operator to approve a pending exec command
/// or plugin action. Covers both <c>exec.approval.requested</c> and
/// <c>plugin.approval.requested</c> upstream events — the
/// <see cref="Kind"/> field discriminates between the two.
/// </summary>
public sealed record ApprovalRequestedNotification(
    string Id,
    string Kind,
    string Title,
    string? Summary,
    string? Command,
    long? CreatedAtMs,
    long? ExpiresAtMs,
    string? SourceChannel) : INotification;
