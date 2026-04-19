using Seren.Infrastructure.OpenClaw.Identity;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Identity;

/// <summary>
/// Byte-for-byte parity with <c>openclaw/src/gateway/device-auth.test.ts</c>.
/// Any divergence from upstream's payload format breaks the Ed25519 signature
/// check on the gateway side, so these vectors must stay locked in sync.
/// </summary>
public sealed class DeviceAuthPayloadBuilderTests
{
    // Inline `new[] { ... }` arrays in [Fact] bodies trigger CA1861 under the
    // repo's AllEnabledByDefault analyzers. Hoisting them to static readonly
    // fields keeps the vectors readable while satisfying the rule.
    private static readonly string[] AdminAndReadScopes = ["operator.admin", "operator.read"];
    private static readonly string[] ReadOnlyScope = ["operator.read"];
    private static readonly string[] UnorderedScopes = ["zeta", "alpha", "mid"];

    [Fact]
    public void BuildV2_MatchesUpstreamCanonicalVector()
    {
        var payload = DeviceAuthPayloadBuilder.BuildV2(
            deviceId: "dev-1",
            clientId: "openclaw-macos",
            clientMode: "ui",
            role: "operator",
            scopes: AdminAndReadScopes,
            signedAtMs: 1_700_000_000_000,
            token: null,
            nonce: "nonce-abc");

        payload.ShouldBe(
            "v2|dev-1|openclaw-macos|ui|operator|operator.admin,operator.read|1700000000000||nonce-abc");
    }

    [Fact]
    public void BuildV3_MatchesUpstreamCanonicalVector_WithTrimmedMetadata()
    {
        var payload = DeviceAuthPayloadBuilder.BuildV3(
            deviceId: "dev-1",
            clientId: "openclaw-macos",
            clientMode: "ui",
            role: "operator",
            scopes: AdminAndReadScopes,
            signedAtMs: 1_700_000_000_000,
            token: "tok-123",
            nonce: "nonce-abc",
            platform: "  IOS  ",
            deviceFamily: "  iPhone  ");

        payload.ShouldBe(
            "v3|dev-1|openclaw-macos|ui|operator|operator.admin,operator.read|1700000000000|tok-123|nonce-abc|ios|iphone");
    }

    [Fact]
    public void BuildV3_KeepsEmptyMetadataSlots_WhenPlatformAndFamilyMissing()
    {
        var payload = DeviceAuthPayloadBuilder.BuildV3(
            deviceId: "dev-2",
            clientId: "openclaw-ios",
            clientMode: "ui",
            role: "operator",
            scopes: ReadOnlyScope,
            signedAtMs: 1_700_000_000_001,
            token: null,
            nonce: "nonce-def",
            platform: null,
            deviceFamily: null);

        payload.ShouldBe("v3|dev-2|openclaw-ios|ui|operator|operator.read|1700000000001||nonce-def||");
    }

    [Fact]
    public void BuildV3_PreservesScopeOrder_DoesNotSort()
    {
        // Upstream uses scopes.join(",") verbatim — order must be preserved.
        var ordered = DeviceAuthPayloadBuilder.BuildV3(
            deviceId: "d", clientId: "c", clientMode: "m", role: "r",
            scopes: UnorderedScopes,
            signedAtMs: 0, token: "", nonce: "n",
            platform: null, deviceFamily: null);

        ordered.ShouldContain("|zeta,alpha,mid|");
    }

    [Fact]
    public void BuildV3_WithEmptyScopes_ProducesEmptyCsvSlot()
    {
        var payload = DeviceAuthPayloadBuilder.BuildV3(
            deviceId: "d", clientId: "c", clientMode: "m", role: "r",
            scopes: Array.Empty<string>(),
            signedAtMs: 42, token: "t", nonce: "n",
            platform: "p", deviceFamily: "f");

        payload.ShouldBe("v3|d|c|m|r||42|t|n|p|f");
    }

    [Fact]
    public void NormalizeMetadata_LowercasesAsciiOnly()
    {
        // Mirrors upstream's toLowerAscii: the Turkish dotted İ passes through
        // untouched because we only shift A–Z.
        DeviceAuthPayloadBuilder.NormalizeMetadata("  İOS  ").ShouldBe("İos");
        DeviceAuthPayloadBuilder.NormalizeMetadata("  MAC  ").ShouldBe("mac");
        DeviceAuthPayloadBuilder.NormalizeMetadata(null).ShouldBe(string.Empty);
        DeviceAuthPayloadBuilder.NormalizeMetadata("   ").ShouldBe(string.Empty);
    }
}
