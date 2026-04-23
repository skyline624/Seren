using FluentValidation;

namespace Seren.Application.Chat;

/// <summary>
/// Resilience policy for the chat-stream pipeline. Bound from the
/// <c>OpenClaw:Chat:Resilience</c> configuration section.
/// </summary>
/// <remarks>
/// Retry is only safe when <b>no</b> content has been delivered to the UI yet
/// — the pipeline tracks a <c>hasDeliveredContent</c> flag internally and
/// refuses to retry once any <c>output:chat:chunk</c> has been broadcast
/// (otherwise the UI would see the same prefix twice and the assistant
/// bubble would duplicate).
/// <para/>
/// When retries on the primary are exhausted (or not configured), the
/// pipeline cascades through <see cref="FallbackModels"/> in declaration
/// order. The very first successful attempt wins; a transparent
/// <c>output:chat:provider-degraded</c> broadcast informs the UI that a
/// different provider ended up producing the reply.
/// </remarks>
public sealed class ChatResilienceOptions
{
    public const string SectionName = "OpenClaw:Chat:Resilience";

    /// <summary>
    /// Number of times to retry the same primary model when the stream goes
    /// idle <b>before</b> the first chunk is delivered. Set to <c>0</c> to
    /// disable same-model retry and cascade straight to fallbacks.
    /// </summary>
    /// <remarks>
    /// Each retry mints a fresh idempotency key so OpenClaw starts a new
    /// run instead of re-subscribing to the dead one. Only the very first
    /// attempt reuses the caller's <c>clientMessageId</c> (so the
    /// multi-tab echo stays coherent).
    /// </remarks>
    public int RetryOnIdleBeforeFirstChunk { get; set; } = 1;

    /// <summary>
    /// Fixed backoff between attempts. Kept small because the scenario is
    /// "provider is slow to answer, try again" not "we hammered the API
    /// and need to cool down". A jittered exponential backoff would only
    /// add complexity without value at this call rate.
    /// </summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Ordered list of fully-qualified provider/model ids to try after
    /// the primary (and its retries) have all failed. Empty means no
    /// fallback — the pipeline surfaces a <c>permanent</c> error once
    /// retries are exhausted.
    /// </summary>
    /// <remarks>
    /// Entries that don't exist in OpenClaw's catalog log a startup
    /// warning and are skipped at runtime; a typo here must not crash
    /// the app.
    /// </remarks>
    public IList<string> FallbackModels { get; set; } = [];
}

/// <summary>Validates <see cref="ChatResilienceOptions"/> at startup.</summary>
public sealed class ChatResilienceOptionsValidator : AbstractValidator<ChatResilienceOptions>
{
    public ChatResilienceOptionsValidator()
    {
        RuleFor(x => x.RetryOnIdleBeforeFirstChunk)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(5)
            .WithMessage("OpenClaw:Chat:Resilience:RetryOnIdleBeforeFirstChunk must be 0..5.");

        RuleFor(x => x.RetryBackoff)
            .Must(t => t >= TimeSpan.Zero && t <= TimeSpan.FromSeconds(10))
            .WithMessage("OpenClaw:Chat:Resilience:RetryBackoff must be 0..10 s.");

        RuleForEach(x => x.FallbackModels)
            .NotEmpty()
            .WithMessage("OpenClaw:Chat:Resilience:FallbackModels entries must be non-empty strings.");
    }
}
