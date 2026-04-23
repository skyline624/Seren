using FluentValidation;

namespace Seren.Application.Chat;

/// <summary>
/// Tunables for the streaming chat path: how long the hub will wait for
/// the next chunk from OpenClaw (<see cref="IdleTimeout"/>) and the
/// absolute ceiling on a single run (<see cref="TotalTimeout"/>).
/// </summary>
/// <remarks>
/// Bound from the <c>OpenClaw:Chat</c> configuration section. Defaults
/// match what we observed against working local + cloud providers:
/// 30 s without a token is generous, and 3 minutes is an unusually long
/// reasoning turn already. Override via <c>OpenClaw__Chat__IdleTimeout</c>
/// / <c>OpenClaw__Chat__TotalTimeout</c> env vars when running tests
/// or specialised providers.
/// <para/>
/// <see cref="TotalTimeout"/> is also forwarded to OpenClaw as the
/// <c>chat.send.timeoutMs</c> parameter, so the gateway itself enforces
/// the cap if Seren ever loses contact with it mid-run.
/// </remarks>
public sealed class ChatStreamOptions
{
    public const string SectionName = "OpenClaw:Chat";

    /// <summary>
    /// Maximum gap allowed between two streamed chunks before the hub
    /// gives up on the run and broadcasts <c>stream_idle_timeout</c>.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Absolute ceiling on a single chat run's total duration. Triggers a
    /// <c>stream_total_timeout</c> broadcast and is forwarded to OpenClaw
    /// as <c>chat.send.timeoutMs</c> so both ends share the same cap.
    /// </summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(3);
}

/// <summary>Validates <see cref="ChatStreamOptions"/> at startup.</summary>
public sealed class ChatStreamOptionsValidator : AbstractValidator<ChatStreamOptions>
{
    public ChatStreamOptionsValidator()
    {
        // ≥ 1 s avoids immediate false positives on slow providers but stays
        // overridable for integration tests that need sub-second horizons.
        RuleFor(x => x.IdleTimeout)
            .Must(t => t >= TimeSpan.FromSeconds(1))
            .WithMessage("OpenClaw:Chat:IdleTimeout must be >= 1 second.");

        RuleFor(x => x.TotalTimeout)
            .Must(t => t >= TimeSpan.FromSeconds(1))
            .WithMessage("OpenClaw:Chat:TotalTimeout must be >= 1 second.");

        RuleFor(x => x)
            .Must(x => x.TotalTimeout >= x.IdleTimeout)
            .WithMessage("OpenClaw:Chat:TotalTimeout must be >= OpenClaw:Chat:IdleTimeout "
                       + "(otherwise the idle window can never fire before the total cap).");
    }
}
