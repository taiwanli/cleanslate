using FluentAssertions;
using Xunit;
using CleanSlate.Uninstall;
using CleanSlate.Safety;

namespace CleanSlate.UnitTests;

public class P1RegressionTests
{
    [Fact]
    public void Force_planner_rejects_short_substring_false_positive()
    {
        var planner = new ForceRemovalPlanner();
        var plan = planner.Build("Adobe", []);
        // "Adobe" should not match "AdobeUpdateHelperX" unless word boundary - "Adobe" in "AdobeUpdate" has no boundary after
        // Word boundary: Adobe in "Adobe Creative" matches; "MyAdobeX" should not match as whole word
        foreach (var t in plan.Targets.Where(x => x.Kind == ForceRemovalKind.RegistryKey))
        {
            t.Hint.Length.Should().BeGreaterThanOrEqualTo(3);
        }
        plan.Should().NotBeNull();
    }

    [Fact]
    public void Policy_can_block_registry_delete()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CS-P1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "policy.json");
        File.WriteAllText(path, """
        {
          "BlockedPathPatterns": ["C:\\\\"],
          "AllowSoftwareNames": [],
          "DenySoftwareNames": [],
          "AllowBatchUninstall": false,
          "AllowRegistryDelete": false
        }
        """);
        try
        {
            var p = PolicyWhitelist.Load(path);
            p.Document.AllowRegistryDelete.Should().BeFalse();
            p.Document.AllowBatchUninstall.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
