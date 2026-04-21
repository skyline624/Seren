using FluentValidation;

namespace Seren.Infrastructure.Ollama;

/// <summary>
/// Settings for Seren's direct Ollama REST client. Kept narrow —
/// the only thing Seren needs from Ollama today is the list of locally
/// installed models, so it can merge them with OpenClaw's cloud catalog
/// before returning <c>GET /api/models</c>.
/// </summary>
/// <remarks>
/// <para>
/// An empty <see cref="BaseUrl"/> is a valid configuration: it tells
/// <see cref="OllamaRestClient"/> to skip Ollama entirely and return an
/// empty list. This keeps dev environments without a local Ollama
/// instance from logging timeout noise every minute.
/// </para>
/// </remarks>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>
    /// Base URL of the Ollama REST API, e.g. <c>http://host.docker.internal:11434</c>.
    /// Empty string disables the integration.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Hard ceiling on the HTTP request for <c>/api/tags</c>. Short by
    /// design — if Ollama isn't responding quickly we'd rather degrade
    /// to an empty list than block <c>/api/models</c> for seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class OllamaOptionsValidator : AbstractValidator<OllamaOptions>
{
    public OllamaOptionsValidator()
    {
        // Either empty (disabled) or a parsable absolute HTTP(S) URI.
        RuleFor(x => x.BaseUrl)
            .Must(url => string.IsNullOrEmpty(url)
                || (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            .WithMessage("Ollama:BaseUrl must be empty or an absolute http(s) URL.");

        RuleFor(x => x.TimeoutSeconds)
            .InclusiveBetween(1, 60)
            .WithMessage("Ollama:TimeoutSeconds must be between 1 and 60.");
    }
}
