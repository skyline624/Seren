using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Timing-safe comparison for OpenClaw authentication tokens.
/// Prevents CWE-208 (timing attack) by using constant-time comparison.
/// </summary>
public sealed class OpenClawTokenValidator
{
    private readonly string _expectedToken;

    public OpenClawTokenValidator(IOptions<OpenClawOptions> options)
    {
        _expectedToken = options.Value.AuthToken;
    }

    /// <summary>
    /// Validates the provided token against the configured <c>OpenClaw:AuthToken</c>
    /// using <see cref="CryptographicOperations.FixedTimeEquals"/> to prevent
    /// timing side-channels (CWE-208).
    /// </summary>
    /// <param name="providedToken">The token received in an incoming request.</param>
    /// <returns><c>true</c> if the tokens match; <c>false</c> otherwise.</returns>
    public bool ValidateToken(string providedToken)
    {
        if (string.IsNullOrEmpty(_expectedToken) || string.IsNullOrEmpty(providedToken))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(_expectedToken),
            Encoding.UTF8.GetBytes(providedToken));
    }
}
