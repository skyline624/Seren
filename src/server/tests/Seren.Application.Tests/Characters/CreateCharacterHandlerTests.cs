using NSubstitute;
using Seren.Application.Abstractions;
using Seren.Application.Characters;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

public sealed class CreateCharacterHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_CreatesAndPersistsCharacter()
    {
        // arrange
        var repository = Substitute.For<ICharacterRepository>();

        var handler = new CreateCharacterHandler(repository);

        var command = new CreateCharacterCommand(
            Name: "Airi",
            SystemPrompt: "You are a friendly AI companion.",
            VrmAssetPath: "/models/airi.vrm",
            Voice: "openai:nova",
            AgentId: "agent-42");

        // act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Airi");
        result.SystemPrompt.ShouldBe("You are a friendly AI companion.");
        result.VrmAssetPath.ShouldBe("/models/airi.vrm");
        result.Voice.ShouldBe("openai:nova");
        result.AgentId.ShouldBe("agent-42");
        result.Id.ShouldNotBe(Guid.Empty);
        result.IsActive.ShouldBeFalse();

        await repository.Received(1).AddAsync(
            Arg.Is<Character>(c =>
                c.Name == "Airi" &&
                c.SystemPrompt == "You are a friendly AI companion." &&
                c.VrmAssetPath == "/models/airi.vrm" &&
                c.Voice == "openai:nova" &&
                c.AgentId == "agent-42"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CommandWithOnlyRequiredFields_CreatesCharacterWithNullOptionals()
    {
        // arrange
        var repository = Substitute.For<ICharacterRepository>();

        var handler = new CreateCharacterHandler(repository);

        var command = new CreateCharacterCommand(
            Name: "Minimal",
            SystemPrompt: "You are minimal.",
            VrmAssetPath: null,
            Voice: null,
            AgentId: null);

        // act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // assert
        result.ShouldNotBeNull();
        result.VrmAssetPath.ShouldBeNull();
        result.Voice.ShouldBeNull();
        result.AgentId.ShouldBeNull();

        await repository.Received(1).AddAsync(
            Arg.Is<Character>(c =>
                c.VrmAssetPath == null &&
                c.Voice == null &&
                c.AgentId == null),
            Arg.Any<CancellationToken>());
    }
}
