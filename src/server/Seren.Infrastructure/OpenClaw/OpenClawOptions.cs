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
    }
}
