using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void InfrastructureAssembly_ShouldLoad()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("Seren.Infrastructure");
    }
}
