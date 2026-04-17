using FluentValidation;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Options for the OpenClaw Gateway adapter, bound from the
/// <c>OpenClaw</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class OpenClawOptions
{
    public const string SectionName = "OpenClaw";

    /// <summary>Base URL of the OpenClaw Gateway REST API.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:18789";

    /// <summary>Authentication token for the <c>Authorization: Bearer</c> header.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>Default agent identifier sent via <c>x-openclaw-model</c> header.</summary>
    public string DefaultAgentId { get; set; } = "openclaw/default";

    /// <summary>
    /// Sub-options for the persistent gateway WebSocket (handshake, RPC, tick
    /// watchdog). Bound from the nested <c>OpenClaw:WebSocket</c> section.
    /// </summary>
    public OpenClawWebSocketOptions WebSocket { get; set; } = new();
}

/// <summary>
/// Tunables for the persistent gateway WebSocket session.
/// </summary>
public sealed class OpenClawWebSocketOptions
{
    /// <summary>
    /// Maximum time allowed between sending the <c>connect</c> request and
    /// receiving the gateway's <c>hello-ok</c> response. Must stay under the
    /// gateway's own preauth cap (10 s).
    /// </summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Default per-call timeout for <c>IOpenClawGateway.CallAsync</c>.</summary>
    public TimeSpan RpcTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier applied to the negotiated <c>tickIntervalMs</c>. If the
    /// client sees no frames for <c>tickIntervalMs * TickGraceMultiplier</c>
    /// it closes the socket with code <c>4000</c> and reconnects.
    /// </summary>
    public double TickGraceMultiplier { get; set; } = 2.0;

    /// <summary>Cap on the exponential backoff between reconnect attempts.</summary>
    public TimeSpan ReconnectMaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Validates <see cref="OpenClawOptions"/> at startup.
/// AuthToken is optional in development (localhost) but required in production.
/// </summary>
public sealed class OpenClawOptionsValidator : AbstractValidator<OpenClawOptions>
{
    public OpenClawOptionsValidator()
    {
        RuleFor(x => x.BaseUrl)
            .NotEmpty().WithMessage("OpenClaw:BaseUrl is required.");

        // AuthToken is optional when Seren talks to OpenClaw over the
        // internal compose network (`http://openclaw:18789`) or to a
        // developer's localhost; any other hostname is treated as
        // production-grade and must carry a token.
        RuleFor(x => x.AuthToken)
            .NotEmpty()
            .When(x => !string.IsNullOrEmpty(x.BaseUrl)
                && !x.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                && !x.BaseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !x.BaseUrl.Contains("openclaw", StringComparison.OrdinalIgnoreCase))
            .WithMessage("OpenClaw:AuthToken is required in production environments.");

        // WebSocket sub-options — sane bounds so a typo in config can't
        // silently break the gateway link or drift into a runaway state.
        RuleFor(x => x.WebSocket.HandshakeTimeout)
            .Must(t => t > TimeSpan.Zero && t <= TimeSpan.FromSeconds(10))
            .WithMessage("OpenClaw:WebSocket:HandshakeTimeout must be between 0 and 10 s "
                       + "(the gateway's own preauth handshake cap is 10 s).");

        RuleFor(x => x.WebSocket.RpcTimeout)
            .Must(t => t > TimeSpan.Zero)
            .WithMessage("OpenClaw:WebSocket:RpcTimeout must be strictly positive.");

        RuleFor(x => x.WebSocket.TickGraceMultiplier)
            .GreaterThan(1.0)
            .WithMessage("OpenClaw:WebSocket:TickGraceMultiplier must be > 1.0 "
                       + "to absorb normal jitter between ticks.");

        RuleFor(x => x.WebSocket.ReconnectMaxBackoff)
            .Must(t => t >= TimeSpan.FromSeconds(1))
            .WithMessage("OpenClaw:WebSocket:ReconnectMaxBackoff must be >= 1 s.");
    }
}
