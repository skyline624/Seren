using Shouldly;
using Xunit;

namespace Seren.Application.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void ApplicationAssembly_ShouldLoad()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("Seren.Application");
    }
}
