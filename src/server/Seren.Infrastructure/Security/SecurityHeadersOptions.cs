namespace Seren.Infrastructure.Security;

/// <summary>
/// Options for the <see cref="SecurityHeadersMiddleware"/>, bound from the
/// <c>SecurityHeaders</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    /// <summary>Whether the middleware is enabled. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The full value of the <c>Content-Security-Policy</c> header. Set to
    /// <c>null</c> or empty to suppress the header.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; } =
        "default-src 'self'; "
      + "connect-src 'self' ws: wss: http: https:; "
      + "img-src 'self' data: blob:; "
      + "media-src 'self' blob:; "
      + "style-src 'self' 'unsafe-inline'; "
      + "script-src 'self'; "
      + "font-src 'self' data:; "
      + "frame-ancestors 'none'; "
      + "base-uri 'self'; "
      + "form-action 'self'";

    /// <summary>Value of <c>X-Frame-Options</c>. Defaults to <c>DENY</c>.</summary>
    public string XFrameOptions { get; set; } = "DENY";

    /// <summary>Value of <c>Referrer-Policy</c>.</summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>Value of <c>Permissions-Policy</c>. Disables cameras/geolocation by default, keeps mic for voice input.</summary>
    public string PermissionsPolicy { get; set; } =
        "microphone=(self), camera=(), geolocation=(), payment=(), usb=()";

    /// <summary>Whether to emit <c>X-Content-Type-Options: nosniff</c>.</summary>
    public bool NoSniff { get; set; } = true;

    /// <summary>
    /// Value of <c>Strict-Transport-Security</c>. Only emitted when the
    /// request is HTTPS. Defaults to 1 year + includeSubDomains.
    /// </summary>
    public string StrictTransportSecurity { get; set; } = "max-age=31536000; includeSubDomains";
}
