using Seren.Application.Sessions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Sessions;

public sealed class AnnouncePeerValidatorTests
{
    private static AnnouncePeerCommand ValidCommand() => new(
        PeerId.New(),
        new AnnouncePayload
        {
            Identity = new ModuleIdentityDto { Id = "web-01", PluginId = "stage-web" },
            Name = "Seren Web",
        },
        "evt-1");

    [Fact]
    public async Task Validate_WithValidCommand_ShouldSucceed()
    {
        var validator = new AnnouncePeerValidator();

        var result = await validator.ValidateAsync(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_WithMissingParentEventId_ShouldFail()
    {
        var validator = new AnnouncePeerValidator();
        var command = ValidCommand() with { ParentEventId = "" };

        var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "ParentEventId");
    }

    [Fact]
    public async Task Validate_WithEmptyName_ShouldFail()
    {
        var validator = new AnnouncePeerValidator();
        var command = ValidCommand() with
        {
            Payload = new AnnouncePayload
            {
                Identity = new ModuleIdentityDto { Id = "web-01", PluginId = "stage-web" },
                Name = "",
            },
        };

        var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Payload.Name");
    }

    [Fact]
    public async Task Validate_WithEmptyPluginId_ShouldFail()
    {
        var validator = new AnnouncePeerValidator();
        var command = ValidCommand() with
        {
            Payload = new AnnouncePayload
            {
                Identity = new ModuleIdentityDto { Id = "web-01", PluginId = "" },
                Name = "Seren Web",
            },
        };

        var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Payload.Identity.PluginId");
    }
}
