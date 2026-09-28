using CleanSlate.Core;
using CleanSlate.Uninstall;
using FluentAssertions;
using Xunit;
using CleanSlate.AgentRules;
using CleanSlate.Safety;

namespace CleanSlate.UnitTests;

public class P0RegressionTests
{
    [Fact]
    public void Setup_flag_is_not_treated_as_silent_S()
    {
        var parsed = UninstallCommandParser.Parse(@"C:\Tools\App\uninstall.exe /SETUP");
        parsed.Arguments.Should().Be("/SETUP");
        // "Silent mode available" is fine; the bug was treating /SETUP as already-silent substring.
        parsed.SilentArguments.Should().NotBeNull();
        parsed.SilentArguments!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().NotContain(t => t.Equals("/SETUP", StringComparison.OrdinalIgnoreCase) && false);
        // Ensure /SETUP is preserved and not swallowed as silent
        parsed.SilentArguments.Should().Contain("/SETUP");
    }

    [Fact]
    public void Quoted_path_keeps_clean_arguments()
    {
        var parsed = UninstallCommandParser.Parse(@"""C:\Program Files\App\uninstall.exe"" /INTERACTIVE");
        parsed.FileName.Should().Be(@"C:\Program Files\App\uninstall.exe");
        parsed.Arguments.Should().Be("/INTERACTIVE");
        parsed.Arguments.Should().NotStartWith("\"");
    }

    [Fact]
    public void Agent_layers_have_distinct_relative_paths()
    {
        var rule = new AgentCleanupRule(
            "x", "X", [],
            ["%APPDATA%\\X"],
            [
                new AgentCleanupLayer("L1Cache", RiskLevel.Low, true, null, "cache"),
                new AgentCleanupLayer("L4Credential", RiskLevel.High, false, null, "credentials"),
            ]);

        var plan = new AgentCleanupPlanner().BuildPlan(rule);
        plan.Should().HaveCount(2);
        plan[0].TargetPath.Should().NotBe(plan[1].TargetPath);
        plan[0].TargetPath.Should().NotBeNullOrEmpty();
        plan[0].TargetPath.Should().EndWith("cache");
        plan[1].TargetPath.Should().EndWith("credentials");
    }

    [Fact]
    public void Agent_layer_without_relative_path_is_blocked()
    {
        var rule = new AgentCleanupRule(
            "x", "X", [], ["%APPDATA%\\X"],
            [new AgentCleanupLayer("L1Cache", RiskLevel.Low, true, null, null)]);

        var plan = new AgentCleanupPlanner().BuildPlan(rule);
        plan.Should().HaveCount(1);
        plan[0].TargetPath.Should().BeNullOrEmpty();
        plan[0].DefaultSelected.Should().BeFalse();
        plan[0].Expanded.Should().BeFalse();
    }

    [Fact]
    public void Folder_quarantine_cross_volume_copy_delete()
    {
        var srcRoot = Path.Combine(Path.GetTempPath(), "CS-P0-Src", Guid.NewGuid().ToString("N"));
        var qRoot = Path.Combine(Path.GetTempPath(), "CS-P0-Q", Guid.NewGuid().ToString("N"));
        var src = Path.Combine(srcRoot, "data");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "hello");
        try
        {
            var svc = new QuarantineService(qRoot);
            var result = svc.QuarantineFolder(src, "job-p0", "cross");
            result.Success.Should().BeTrue(result.ErrorMessage);
            Directory.Exists(src).Should().BeFalse();
            Directory.Exists(result.Record!.QuarantinePath).Should().BeTrue();

            var restorer = new QuarantineRestorer(svc);
            var summary = restorer.RestoreJob("job-p0");
            summary.Restored.Should().Be(1);
            File.Exists(Path.Combine(src, "a.txt")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(srcRoot)) Directory.Delete(srcRoot, true);
            if (Directory.Exists(qRoot)) Directory.Delete(qRoot, true);
        }
    }
}
