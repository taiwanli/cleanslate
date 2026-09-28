using CleanSlate.Core;
using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class ModelsTests
{
    [Fact]
    public void RiskLevel_Protected_Exists()
    {
        RiskLevel.Protected.ToString().Should().Be("Protected");
    }
}
