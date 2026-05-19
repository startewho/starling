using FluentAssertions;
using Xunit;

namespace Starling.Layout.Tests;

public class PlaceholderSmokeTests
{
    [Fact]
    public void Project_loads_and_xunit_runs()
    {
        // Reference the placeholder so the project reference isn't unused.
        typeof(Starling.Layout.PlaceholderNote).Should().NotBeNull();
    }
}
