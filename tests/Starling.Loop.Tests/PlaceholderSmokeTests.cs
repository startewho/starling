using FluentAssertions;
using Xunit;

namespace Starling.Loop.Tests;

public class PlaceholderSmokeTests
{
    [Fact]
    public void Project_loads_and_xunit_runs()
    {
        // Reference the placeholder so the project reference isn't unused.
        typeof(Starling.Loop.PlaceholderNote).Should().NotBeNull();
    }
}
