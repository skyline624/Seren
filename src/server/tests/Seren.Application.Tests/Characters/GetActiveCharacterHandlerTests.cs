using NSubstitute;
using Seren.Application.Abstractions;
using Seren.Application.Characters;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

public sealed class GetActiveCharacterHandlerTests
{
    [Fact]
    public async Task Handle_WhenActiveCharacterExists_ReturnsIt()
    {
        // arrange
        var character = Character.Create("Test Character", "You are a helpful assistant.") with { IsActive = true };

        var repository = Substitute.For<ICharacterRepository>();
        repository.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(character);

        var handler = new GetActiveCharacterHandler(repository);

        // act
        var result = await handler.Handle(new GetActiveCharacterQuery(), TestContext.Current.CancellationToken);

        // assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(character.Id);
        result.Name.ShouldBe("Test Character");
        result.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenNoActiveCharacter_ReturnsNull()
    {
        // arrange
        var repository = Substitute.For<ICharacterRepository>();
        repository.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Character?)null);

        var handler = new GetActiveCharacterHandler(repository);

        // act
        var result = await handler.Handle(new GetActiveCharacterQuery(), TestContext.Current.CancellationToken);

        // assert
        result.ShouldBeNull();
    }
}
