using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Chat;

/// <summary>
/// Default <see cref="IChatStreamPipeline"/>: applies idle/total timeouts,
/// retries the primary model on idle-before-first-chunk, cascades through
/// configured fallback models, and emits the canonical terminal broadcasts.
/// </summary>
/// <remarks>
/// The retry loop is deliberately small and unconditional: the only decision
/// it makes is "same model again, next fallback, or give up" based on how
/// many attempts remain and whether any content was delivered. All the
/// scary bits (CTS lifecycle, idempotency key rotation, best-effort upstream
/// abort) live here once so the handlers stay focused on domain logic.
/// </remarks>
public sealed class ChatStreamPipeline : IChatStreamPipeline
{
    private readonly IOpenClawChat _chat;
    private readonly ISerenHub _hub;
    private readonly IChatRunRegistry _runRegistry;
    private readonly ChatStreamOptions _streamOptions;
    private readonly ChatResilienceOptions _resilienceOptions;
    private readonly ChatStreamMetrics _metrics;
    private readonly ILogger<ChatStreamPipeline> _logger;

    public ChatStreamPipeline(
        IOpenClawChat chat,
        ISerenHub hub,
        IChatRunRegistry runRegistry,
        IOptions<ChatStreamOptions> streamOptions,
        IOptions<ChatResilienceOptions> resilienceOptions,
        ChatStreamMetrics metrics,
        ILogger<ChatStreamPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(streamOptions);
        ArgumentNullException.ThrowIfNull(resilienceOptions);

        _chat = chat;
        _hub = hub;
        _runRegistry = runRegistry;
        _streamOptions = streamOptions.Value;
        _resilienceOptions = resilienceOptions.Value;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatStreamOutcome> RunAsync(
        ChatStreamRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempts = BuildAttemptPlan(request);

        var stopwatch = Stopwatch.StartNew();
        var attemptsMade = 0;
        string runId = string.Empty;
        var modelUsed = request.PrimaryModel ?? string.Empty;
        var outcome = ChatStreamOutcomes.Error;
        var hasDeliveredContent = false;

        for (var i = 0; i < attempts.Count; i++)
        {
            var (model, idempotencyKey) = attempts[i];
            attemptsMade++;
            modelUsed = model ?? request.PrimaryModel ?? "<default>";

            if (i > 0)
            {
                // Inform peers we're transparently switching. From/To are
                // informational; reason mirrors what caused the previous
                // attempt to fail.
                await BroadcastDegradedAsync(
                    from: ResolveAttemptLabel(attempts, i - 1, request.PrimaryModel),
                    to: ResolveAttemptLabel(attempts, i, request.PrimaryModel),
                    reason: outcome,
                    attempt: i,
                    cancellationToken).ConfigureAwait(false);

                _metrics.RecordFallback(
                    fromProvider: ResolveAttemptLabel(attempts, i - 1, request.PrimaryModel),
                    toProvider: modelUsed,
                    reason: outcome);

                if (_resilienceOptions.RetryBackoff > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(_resilienceOptions.RetryBackoff, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        outcome = ChatStreamOutcomes.UserAbort;
                        break;
                    }
                }
            }

            var attemptResult = await RunAttemptAsync(
                request, model, idempotencyKey, hasDeliveredContent, cancellationToken)
                .ConfigureAwait(false);

            runId = attemptResult.RunId;
            outcome = attemptResult.Outcome;
            hasDeliveredContent = attemptResult.HasDeliveredContent;

            // Retry decision: only re-loop for idle-timeout before any content
            // has reached the UI. Every other outcome is terminal.
            var isRetryableIdle =
                outcome == ChatStreamOutcomes.IdleTimeout
                && !hasDeliveredContent;

            if (!isRetryableIdle)
            {
                break;
            }

            if (i + 1 >= attempts.Count)
            {
                // Retries exhausted and no fallback remaining — give up.
                break;
            }
        }

        stopwatch.Stop();

        await RunTeardownCallbackAsync(request, cancellationToken).ConfigureAwait(false);

        if (outcome == ChatStreamOutcomes.Ok && request.OnSuccess is not null)
        {
            await RunSuccessCallbackAsync(request, cancellationToken).ConfigureAwait(false);
        }

        await BroadcastTerminalEnvelopesAsync(
            outcome, modelUsed, attemptsMade, attempts.Count, request.CharacterId)
            .ConfigureAwait(false);

        _metrics.RecordDuration(stopwatch.Elapsed, modelUsed, outcome);
        _metrics.RecordOutcome(modelUsed, outcome, attemptsMade);

        _logger.LogInformation(
            "Chat stream completed for session {SessionKey} (runId={RunId}, model={Model}, attempts={Attempts}, outcome={Outcome}, elapsedMs={ElapsedMs})",
            request.SessionKey, runId, modelUsed, attemptsMade, outcome, stopwatch.Elapsed.TotalMilliseconds);

        return new ChatStreamOutcome(
            RunId: runId,
            ModelUsed: modelUsed,
            AttemptsMade: attemptsMade,
            Outcome: outcome);
    }

    /// <summary>
    /// Runs a single attempt: StartAsync → SubscribeAsync with idle/total CTS
    /// → best-effort upstream abort on cancellation. Returns the outcome and
    /// whether any content crossed the wire during this attempt (the pipeline
    /// aggregates the flag across attempts to decide if retry is still safe).
    /// </summary>
    private async Task<AttemptResult> RunAttemptAsync(
        ChatStreamRequest request,
        string? model,
        string? idempotencyKey,
        bool alreadyDeliveredContent,
        CancellationToken cancellationToken)
    {
        string runId;
        try
        {
            runId = await _chat.StartAsync(
                request.SessionKey, request.UserText, model, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Provider start may throw any transport exception; treat as attempt failure.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "chat.send failed (model={Model})", model);
            return new AttemptResult(string.Empty, ChatStreamOutcomes.Error, alreadyDeliveredContent);
        }
#pragma warning restore CA1031

        _runRegistry.Register(request.SessionKey, runId);

        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(_streamOptions.TotalTimeout);

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
        streamCts.CancelAfter(_streamOptions.IdleTimeout);

        var hasDeliveredContent = alreadyDeliveredContent;
        var attemptOutcome = ChatStreamOutcomes.Error;

        try
        {
            await foreach (var delta in _chat.SubscribeAsync(runId, streamCts.Token)
                .ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(delta.Content))
                {
                    if (delta.FinishReason is not null)
                    {
                        attemptOutcome = ChatStreamOutcomes.Ok;
                        return new AttemptResult(runId, attemptOutcome, hasDeliveredContent);
                    }
                    continue;
                }

                // A non-empty content chunk arrived — reset the idle window
                // and remember we crossed the "can no longer retry" line.
                streamCts.CancelAfter(_streamOptions.IdleTimeout);
                hasDeliveredContent = true;

                await request.OnContent(delta.Content, cancellationToken).ConfigureAwait(false);
            }

            // Enumerator completed without a FinishReason delta — treat as
            // a clean end but log at debug so unusual upstream behavior
            // stays visible.
            _logger.LogDebug(
                "Chat run {RunId} ended without explicit FinishReason", runId);
            attemptOutcome = ChatStreamOutcomes.Ok;
            return new AttemptResult(runId, attemptOutcome, hasDeliveredContent);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            attemptOutcome = ClassifyCancellation(totalCts, streamCts);

            _logger.LogWarning(
                "Chat run {RunId} ended with {Outcome} (model={Model})",
                runId, attemptOutcome, model);

            await BestEffortAbortAsync(request.SessionKey, runId).ConfigureAwait(false);
            return new AttemptResult(runId, attemptOutcome, hasDeliveredContent);
        }
#pragma warning disable CA1031 // Unexpected exception on a single attempt — return as error outcome.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Chat run {RunId} threw unexpectedly (model={Model})", runId, model);

            await BestEffortAbortAsync(request.SessionKey, runId).ConfigureAwait(false);
            return new AttemptResult(runId, ChatStreamOutcomes.Error, hasDeliveredContent);
        }
#pragma warning restore CA1031
        finally
        {
            if (!string.IsNullOrEmpty(runId))
            {
                _runRegistry.Unregister(request.SessionKey, runId);
            }
        }
    }

    private async Task RunTeardownCallbackAsync(ChatStreamRequest request, CancellationToken ct)
    {
        if (request.OnTeardown is null)
        {
            return;
        }

        try
        {
            await request.OnTeardown(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Teardown must not mask the real outcome.
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OnTeardown callback threw during chat stream finalize.");
        }
#pragma warning restore CA1031
    }

    private async Task RunSuccessCallbackAsync(ChatStreamRequest request, CancellationToken ct)
    {
        try
        {
            await request.OnSuccess!(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // OnSuccess failure should not prevent the terminal broadcast.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnSuccess callback threw; continuing with stream end broadcast.");
        }
#pragma warning restore CA1031
    }

    private async Task BroadcastTerminalEnvelopesAsync(
        string outcome, string modelUsed, int attemptsMade, int totalAttempts, string? characterId)
    {
        // Error envelope for the terminal-failure cases. User abort is
        // intentionally silent. Timeouts use their own code + message pair;
        // "error" outcome falls back to a generic category.
        if (outcome != ChatStreamOutcomes.Ok && outcome != ChatStreamOutcomes.UserAbort)
        {
            var (code, message, category) = MapOutcomeToError(outcome, attemptsMade, totalAttempts);
            await BroadcastEnvelopeSafelyAsync(
                CreateEnvelope(
                    EventTypes.Error,
                    new ErrorPayload
                    {
                        Code = code,
                        Message = message,
                        Category = category,
                        FailedProvider = modelUsed,
                    }),
                CancellationToken.None).ConfigureAwait(false);
        }

        await BroadcastEnvelopeSafelyAsync(
            CreateEnvelope(
                EventTypes.OutputChatEnd,
                new ChatEndPayload { CharacterId = characterId }),
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Builds the ordered attempt plan: primary + retries, then each fallback once.</summary>
    private List<(string? Model, string? IdempotencyKey)> BuildAttemptPlan(ChatStreamRequest request)
    {
        var plan = new List<(string? Model, string? IdempotencyKey)>
        {
            // Attempt 0: primary + client-supplied idempotency key (if any).
            // Using the client's key preserves the multi-tab echo invariant.
            (request.PrimaryModel, request.ClientMessageId),
        };

        for (var i = 0; i < _resilienceOptions.RetryOnIdleBeforeFirstChunk; i++)
        {
            // Retries on the primary use fresh GUIDs — otherwise OpenClaw
            // returns status:"in_flight" and we'd resubscribe to the dead run.
            plan.Add((request.PrimaryModel, null));
        }

        foreach (var fallback in _resilienceOptions.FallbackModels)
        {
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                plan.Add((fallback, null));
            }
        }

        return plan;
    }

    private static string ResolveAttemptLabel(
        List<(string? Model, string? IdempotencyKey)> plan,
        int index,
        string? primaryFallback)
    {
        var model = plan[index].Model ?? primaryFallback;
        return string.IsNullOrWhiteSpace(model) ? "<default>" : model;
    }

    private static string ClassifyCancellation(CancellationTokenSource totalCts, CancellationTokenSource streamCts)
    {
        if (totalCts.IsCancellationRequested)
        {
            return ChatStreamOutcomes.TotalTimeout;
        }
        if (streamCts.IsCancellationRequested)
        {
            return ChatStreamOutcomes.IdleTimeout;
        }
        return ChatStreamOutcomes.UserAbort;
    }

    private static (string Code, string Message, string Category) MapOutcomeToError(
        string outcome, int attemptsMade, int totalAttempts)
    {
        var allExhausted = attemptsMade >= totalAttempts;
        var category = allExhausted ? StreamErrorCategory.Permanent : StreamErrorCategory.Transient;

        return outcome switch
        {
            ChatStreamOutcomes.IdleTimeout => (
                "stream_idle_timeout",
                "The model stopped responding mid-stream.",
                category),
            ChatStreamOutcomes.TotalTimeout => (
                "stream_total_timeout",
                "The model did not finish within the maximum allowed run time.",
                StreamErrorCategory.Permanent),
            _ => (
                "stream_error",
                "The chat stream could not be completed.",
                category),
        };
    }

    private async Task BroadcastDegradedAsync(
        string from, string to, string reason, int attempt, CancellationToken cancellationToken)
    {
        var envelope = CreateEnvelope(
            EventTypes.OutputChatProviderDegraded,
            new ChatProviderDegradedPayload
            {
                From = from,
                To = to,
                Reason = reason,
                Attempt = attempt,
            });

        await BroadcastEnvelopeSafelyAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task BestEffortAbortAsync(string sessionKey, string runId)
    {
        if (string.IsNullOrEmpty(runId))
        {
            return;
        }

        try
        {
            await _chat.AbortAsync(sessionKey, runId, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Best-effort abort; gateway may have already closed the run.
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "chat.abort failed for runId {RunId} (best-effort).", runId);
        }
#pragma warning restore CA1031
    }

    private async Task BroadcastEnvelopeSafelyAsync(WebSocketEnvelope envelope, CancellationToken ct)
    {
        try
        {
            await _hub.BroadcastAsync(envelope, null, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Teardown writes must never propagate failures.
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to broadcast {EventType} during chat stream.", envelope.Type);
        }
#pragma warning restore CA1031
    }

    private static WebSocketEnvelope CreateEnvelope(string eventType, object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), CamelCaseOptions);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Clone();

        return new WebSocketEnvelope
        {
            Type = eventType,
            Data = data,
            Metadata = new EventMetadata
            {
                Source = HubSource,
                Event = new EventIdentity { Id = Guid.NewGuid().ToString("N") },
            },
        };
    }

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ModuleIdentityDto HubSource = new() { Id = "seren-hub", PluginId = "seren" };

    /// <summary>Return value of <see cref="RunAttemptAsync"/>.</summary>
    private sealed record AttemptResult(string RunId, string Outcome, bool HasDeliveredContent);
}
