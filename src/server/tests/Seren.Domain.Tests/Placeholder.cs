using Shouldly;
using Xunit;

namespace Seren.Domain.Tests;

/// <summary>
/// Smoke test that ensures the Domain assembly loads and the test runner works.
/// </summary>
public sealed class AssemblyMarkerTests
{
    [Fact]
    public void DomainAssembly_ShouldLoad()
    {
        // arrange
        var marker = typeof(AssemblyMarker);

        // act
        var assembly = marker.Assembly;

        // assert
        assembly.ShouldNotBeNull();
        assembly.GetName().Name.ShouldBe("Seren.Domain");
    }
}
