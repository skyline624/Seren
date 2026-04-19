using System.Text;

namespace Seren.Infrastructure.OpenClaw.Identity;

/// <summary>
/// Builds the pipe-delimited device-auth payload string that the OpenClaw
/// gateway signs against the declared public key. Mirror-implemented from
/// <c>openclaw/src/gateway/device-auth.ts:20-54</c> and the metadata
/// normalization in <c>device-metadata-normalization.ts</c>.
/// </summary>
/// <remarks>
/// Every character counts: any divergence (extra whitespace, case change,
/// reordered scope, wrong field order) produces a different UTF-8 payload
/// and the Ed25519 signature will be rejected by the gateway. See the
/// Shouldly-backed test vectors for the exact byte-for-byte shape.
/// </remarks>
internal static class DeviceAuthPayloadBuilder
{
    /// <summary>
    /// Builds the V3 payload with client platform / deviceFamily metadata.
    /// Prefer this over V2 — the gateway tries V3 first when verifying.
    /// </summary>
    public static string BuildV3(
        string deviceId,
        string clientId,
        string clientMode,
        string role,
        IReadOnlyList<string>? scopes,
        long signedAtMs,
        string? token,
        string nonce,
        string? platform,
        string? deviceFamily)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(clientMode);
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        var sb = new StringBuilder(capacity: 256);
        sb.Append("v3|");
        sb.Append(deviceId);
        sb.Append('|');
        sb.Append(clientId);
        sb.Append('|');
        sb.Append(clientMode);
        sb.Append('|');
        sb.Append(role);
        sb.Append('|');
        AppendScopesCsv(sb, scopes);
        sb.Append('|');
        sb.Append(signedAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append(token ?? string.Empty);
        sb.Append('|');
        sb.Append(nonce);
        sb.Append('|');
        sb.Append(NormalizeMetadata(platform));
        sb.Append('|');
        sb.Append(NormalizeMetadata(deviceFamily));
        return sb.ToString();
    }

    /// <summary>
    /// Builds the V2 payload (no platform / deviceFamily) — older clients
    /// that can't send V3 fall back to this shape. Kept for parity with
    /// upstream but Seren always emits V3.
    /// </summary>
    public static string BuildV2(
        string deviceId,
        string clientId,
        string clientMode,
        string role,
        IReadOnlyList<string>? scopes,
        long signedAtMs,
        string? token,
        string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(clientMode);
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        var sb = new StringBuilder(capacity: 192);
        sb.Append("v2|");
        sb.Append(deviceId);
        sb.Append('|');
        sb.Append(clientId);
        sb.Append('|');
        sb.Append(clientMode);
        sb.Append('|');
        sb.Append(role);
        sb.Append('|');
        AppendScopesCsv(sb, scopes);
        sb.Append('|');
        sb.Append(signedAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append(token ?? string.Empty);
        sb.Append('|');
        sb.Append(nonce);
        return sb.ToString();
    }

    private static void AppendScopesCsv(StringBuilder sb, IReadOnlyList<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0)
        {
            return;
        }

        // Upstream uses `scopes.join(",")` verbatim — no sort, no dedupe.
        // Preserve the exact order the caller passed so signatures match.
        for (var i = 0; i < scopes.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(scopes[i]);
        }
    }

    /// <summary>
    /// Mirrors <c>normalizeDeviceMetadataForAuth</c>: trim, then lowercase
    /// ASCII A–Z only (no Unicode case folding, no diacritic stripping —
    /// kept ASCII-only so TS/Swift/Kotlin implementations stay in sync).
    /// Null/whitespace → empty string.
    /// </summary>
    internal static string NormalizeMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        Span<char> buffer = trimmed.Length <= 128
            ? stackalloc char[trimmed.Length]
            : new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            buffer[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
        }
        return new string(buffer);
    }
}
