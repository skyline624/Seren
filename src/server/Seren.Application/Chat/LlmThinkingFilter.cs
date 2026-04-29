namespace Seren.Application.Chat;

/// <summary>
/// Stateless helpers that strip <c>&lt;think&gt;…&lt;/think&gt;</c> and the
/// <c>&lt;action:think&gt;…&lt;/action:think&gt;</c> alias from a streamed
/// LLM response, threading the open/close state across chunk boundaries.
/// </summary>
/// <remarks>
/// <para>
/// The handlers that consume an OpenClaw chat stream (<c>SendTextMessageHandler</c>
/// for the text path, <c>SubmitVoiceInputHandler</c> for the voice path)
/// both need identical reasoning-block filtering: a single bug here used to
/// swallow every visible byte for the rest of a run when a closing tag
/// straddled a chunk boundary. Centralising the state machine guarantees
/// the two paths cannot drift out of sync again.
/// </para>
/// <para>
/// The filter is purely lexical — it does not parse the LLM grammar, only
/// the textual tag delimiters. It is safe to run on partial input: trailing
/// bytes that look like the start of a tag (open or close) are preserved
/// in <see cref="ThinkingTransition.Remainder"/> and meant to be prepended
/// to the next chunk by the caller.
/// </para>
/// </remarks>
public static class LlmThinkingFilter
{
    public const string ThinkOpen = "<think>";
    public const string ThinkClose = "</think>";
    public const string ActionThinkOpen = "<action:think>";
    public const string ActionThinkClose = "</action:think>";

    /// <summary>
    /// Scans <paramref name="buffer"/> for opening / closing thinking tags
    /// (canonical <c>&lt;think&gt;</c> and the <c>&lt;action:think&gt;</c>
    /// alias) that may be split across streamed chunks. Returns the visible
    /// portion ready to forward, the unconsumed suffix for the next chunk,
    /// and the transitions (true = entered thinking, false = left thinking).
    /// </summary>
    public static (string Visible, ThinkingTransition Transition) ExtractThinkingSegments(
        string buffer, ref bool isThinking)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var visible = new System.Text.StringBuilder();
        var transitions = new List<bool>();
        var index = 0;

        while (index < buffer.Length)
        {
            if (isThinking)
            {
                var closeA = buffer.IndexOf(ThinkClose, index, StringComparison.Ordinal);
                var closeB = buffer.IndexOf(ActionThinkClose, index, StringComparison.Ordinal);
                var close = MinNonNeg(closeA, closeB);
                if (close < 0)
                {
                    // No full close tag in the buffer yet. The reasoning
                    // content itself is discarded (never user-visible),
                    // but a partial close at the tail ("</", "</action:thi"
                    // …) MUST be preserved as remainder so the next
                    // streamed chunk can complete the match. Without this
                    // a close split across two chunks is silently lost
                    // and isThinking stays true for the rest of the run,
                    // swallowing every visible byte downstream.
                    index = SafeThinkingEnd(buffer, index);
                    break;
                }
                var closeLen = buffer.AsSpan(close).StartsWith(ThinkClose.AsSpan())
                    ? ThinkClose.Length
                    : ActionThinkClose.Length;
                index = close + closeLen;
                isThinking = false;
                transitions.Add(false);
            }
            else
            {
                var openA = buffer.IndexOf(ThinkOpen, index, StringComparison.Ordinal);
                var openB = buffer.IndexOf(ActionThinkOpen, index, StringComparison.Ordinal);
                var open = MinNonNeg(openA, openB);
                if (open < 0)
                {
                    var safeEnd = SafeVisibleEnd(buffer, index);
                    visible.Append(buffer, index, safeEnd - index);
                    index = safeEnd;
                    break;
                }
                var openLen = buffer.AsSpan(open).StartsWith(ThinkOpen.AsSpan())
                    ? ThinkOpen.Length
                    : ActionThinkOpen.Length;
                visible.Append(buffer, index, open - index);
                index = open + openLen;
                isThinking = true;
                transitions.Add(true);
            }
        }

        return (visible.ToString(), new ThinkingTransition(transitions, buffer[index..]));
    }

    /// <summary>
    /// Returns the earliest position where the buffer suffix could be the
    /// start of a thinking opening tag. Used while OUTSIDE thinking mode
    /// so a partial open ("&lt;thi", "&lt;action:") never lands in
    /// user-visible text.
    /// </summary>
    public static int SafeVisibleEnd(string buffer, int start)
    {
        return SafeTagEnd(buffer, start, [ThinkOpen, ActionThinkOpen]);
    }

    /// <summary>
    /// Mirror of <see cref="SafeVisibleEnd"/> for the thinking branch:
    /// returns the earliest position where the buffer could be the start
    /// of a closing tag. The caller uses this as the new index so the
    /// partial close is kept in <see cref="ThinkingTransition.Remainder"/>
    /// and the next chunk can complete it.
    /// </summary>
    public static int SafeThinkingEnd(string buffer, int start)
    {
        return SafeTagEnd(buffer, start, [ThinkClose, ActionThinkClose]);
    }

    private static int SafeTagEnd(string buffer, int start, string[] tags)
    {
        var earliestSafe = buffer.Length;
        foreach (var tag in tags)
        {
            for (var prefixLen = Math.Min(tag.Length - 1, buffer.Length - start); prefixLen > 0; prefixLen--)
            {
                var candidateStart = buffer.Length - prefixLen;
                if (candidateStart < start)
                {
                    break;
                }

                var span = buffer.AsSpan(candidateStart, prefixLen);
                if (span.SequenceEqual(tag.AsSpan(0, prefixLen)))
                {
                    if (candidateStart < earliestSafe)
                    {
                        earliestSafe = candidateStart;
                    }
                    break;
                }
            }
        }
        return earliestSafe;
    }

    private static int MinNonNeg(int a, int b)
    {
        if (a < 0)
        {
            return b;
        }
        if (b < 0)
        {
            return a;
        }
        return Math.Min(a, b);
    }
}

/// <summary>
/// Outcome of one <see cref="LlmThinkingFilter.ExtractThinkingSegments"/>
/// invocation.
/// </summary>
/// <param name="Events">
/// Boolean transitions in the order they happened: <c>true</c> = entered
/// thinking (caller should broadcast a <c>thinking:start</c> event),
/// <c>false</c> = left thinking (broadcast <c>thinking:end</c>).
/// </param>
/// <param name="Remainder">Tail of the buffer that has not been consumed —
/// either a partial open tag (when not thinking) or a partial close tag
/// (when thinking). The caller must prepend it to the next chunk.</param>
public sealed record ThinkingTransition(IReadOnlyList<bool> Events, string Remainder);
