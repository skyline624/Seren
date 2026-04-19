using Microsoft.Extensions.Logging.Abstractions;
using Seren.Infrastructure.OpenClaw.Identity;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Identity;

public sealed class FileSystemDeviceIdentityStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileSystemDeviceIdentityStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "seren-identity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "seren-device-identity.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task LoadOrCreate_WhenFileMissing_GeneratesAndPersistsKeypair()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);

        var identity = await store.LoadOrCreateAsync(ct);

        identity.PublicKey.Length.ShouldBe(Ed25519Signer.PublicKeySize);
        identity.PrivateKey.Length.ShouldBe(Ed25519Signer.PrivateKeySeedSize);
        identity.DeviceId.Length.ShouldBe(64); // hex sha-256
        identity.DeviceId.ShouldBe(DeviceIdentity.ComputeDeviceId(identity.PublicKey));
        File.Exists(_path).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadOrCreate_OnSecondCall_ReturnsTheSameIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store1 = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);
        var first = await store1.LoadOrCreateAsync(ct);

        using var store2 = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);
        var second = await store2.LoadOrCreateAsync(ct);

        second.DeviceId.ShouldBe(first.DeviceId);
        second.PublicKey.ShouldBe(first.PublicKey);
        second.PrivateKey.ShouldBe(first.PrivateKey);
        second.CreatedAtMs.ShouldBe(first.CreatedAtMs);
    }

    [Fact]
    public async Task LoadOrCreate_WhenCalledTwiceOnSameInstance_CachesIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);

        var a = await store.LoadOrCreateAsync(ct);
        var b = await store.LoadOrCreateAsync(ct);

        ReferenceEquals(a, b).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadOrCreate_WhenFileMalformed_ThrowsClearInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(_path, "{ not-json", ct);
        using var store = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.LoadOrCreateAsync(ct));
        ex.Message.ShouldContain("malformed");
        ex.Message.ShouldContain(_path);
    }

    [Fact]
    public async Task LoadOrCreate_WhenFileEmpty_ThrowsClearInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(_path, "null", ct);
        using var store = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.LoadOrCreateAsync(ct));
        ex.Message.ShouldContain("empty");
    }

    [Fact]
    public async Task GeneratedIdentity_ProducesSignaturesThatVerifyWithItsOwnPublicKey()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileSystemDeviceIdentityStore(
            _path, NullLogger<FileSystemDeviceIdentityStore>.Instance);

        var identity = await store.LoadOrCreateAsync(ct);
        var sig = Ed25519Signer.Sign(identity.PrivateKey, "hello from seren");

        Ed25519Signer.Verify(identity.PublicKey, "hello from seren", sig).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadOrCreate_CreatesMissingDirectoryOnDemand()
    {
        var ct = TestContext.Current.CancellationToken;
        var nested = Path.Combine(_dir, "sub1", "sub2", "identity.json");
        using var store = new FileSystemDeviceIdentityStore(
            nested, NullLogger<FileSystemDeviceIdentityStore>.Instance);

        await store.LoadOrCreateAsync(ct);
        File.Exists(nested).ShouldBeTrue();
    }
}
