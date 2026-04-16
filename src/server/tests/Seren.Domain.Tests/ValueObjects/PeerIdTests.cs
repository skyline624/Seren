using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Domain.Tests.ValueObjects;

public sealed class PeerIdTests
{
    [Fact]
    public void New_ShouldProduceUniqueValues()
    {
        // arrange + act
        var a = PeerId.New();
        var b = PeerId.New();

        // assert
        a.ShouldNotBe(b);
        a.Value.ShouldNotBeNullOrWhiteSpace();
        a.Value.Length.ShouldBe(32); // compact guid
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("abc123", true)]
    public void IsValid_ShouldRejectEmptyAndNullValues(string? input, bool expected)
    {
        PeerId.IsValid(input).ShouldBe(expected);
    }

    [Fact]
    public void Equality_ShouldBeValueBased()
    {
        var a = new PeerId("same");
        var b = new PeerId("same");
        var c = new PeerId("different");

        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
