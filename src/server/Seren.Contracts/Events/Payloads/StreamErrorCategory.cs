namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Error taxonomy for chat-stream failures surfaced to the UI. A plain
/// constants class (rather than an enum) keeps the wire format as a
/// simple string — the TypeScript side models it as a
/// <c>'transient' | 'degraded' | 'permanent'</c> union, which is the
/// idiomatic Vue/TS shape and survives JSON round-trips without enum
/// marshalling gymnastics.
/// </summary>
/// <remarks>
/// The UI uses the category to pick a remediation affordance:
/// <list type="bullet">
/// <item><description><see cref="Transient"/> — show a Retry button; the server decided the
/// failure is probably not going to persist (single timeout spike, bad
/// luck on one provider).</description></item>
/// <item><description><see cref="Degraded"/> — inform the user that Seren has transparently
/// switched to a fallback provider and the answer below may come from a
/// different model than the one they selected.</description></item>
/// <item><description><see cref="Permanent"/> — every configured provider has failed; advise
/// the user to change model or contact support.</description></item>
/// </list>
/// </remarks>
public static class StreamErrorCategory
{
    /// <summary>UI should offer Retry; failure is expected to be intermittent.</summary>
    public const string Transient = "transient";

    /// <summary>UI should inform the user of a successful transparent fallback.</summary>
    public const string Degraded = "degraded";

    /// <summary>UI should advise model change or support; all recovery paths exhausted.</summary>
    public const string Permanent = "permanent";
}
