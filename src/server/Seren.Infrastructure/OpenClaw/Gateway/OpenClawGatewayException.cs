namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Thrown when the OpenClaw gateway returns a frame-level error (RPC call
/// fails, handshake rejected, or protocol contract violated).
/// </summary>
public sealed class OpenClawGatewayException : Exception
{
    /// <summary>Error code reported by the gateway (e.g. <c>INVALID_REQUEST</c>) or a Seren-side tag.</summary>
    public string Code { get; }

    /// <summary>Optional hint from the gateway suggesting whether a retry is worthwhile.</summary>
    public bool? Retryable { get; }

    /// <summary>Optional backoff hint reported by the gateway when <see cref="Retryable"/> is <c>true</c>.</summary>
    public int? RetryAfterMs { get; }

    public OpenClawGatewayException(string code, string message, bool? retryable = null, int? retryAfterMs = null)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
        RetryAfterMs = retryAfterMs;
    }

    public OpenClawGatewayException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
