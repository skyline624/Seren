using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seren.Infrastructure.OpenClaw.Identity;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the on-disk
/// device-identity record. Separate from the wire-shaped records in the
/// Gateway/ namespace because this shape is purely internal (never sent
/// over the wire).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(PersistedDeviceIdentity))]
internal sealed partial class DeviceIdentityJsonContext : JsonSerializerContext;

/// <summary>
/// On-disk representation of a device identity. Byte arrays are serialised
/// as base64url strings so the JSON file is safely editable by hand if
/// necessary (no raw binary).
/// </summary>
/// <remarks>
/// <see cref="PairedAtMs"/> is set after the one-shot bootstrap pairing
/// handshake completes. Subsequent boots skip the bootstrap step when the
/// marker is present — keeping the identity stable and avoiding accidental
/// re-consumption of the bootstrap token.
/// </remarks>
internal sealed record PersistedDeviceIdentity(
    string DeviceId,
    string PublicKey,     // base64url
    string PrivateKey,    // base64url
    long CreatedAtMs,
    long? PairedAtMs = null);
