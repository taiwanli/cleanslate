using FluentAssertions;
using Xunit;
using CleanSlate.AgentRules;
using CleanSlate.Uninstall;

namespace CleanSlate.UnitTests;

public class ForceRemovalExecutorTests
{
    [Fact]
    public void Execute_quarantines_file_and_skips_protected()
    {
        var qRoot = Path.Combine(Path.GetTempPath(), "CleanSlateForceEx", Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "CleanSlateForceWork", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var victim = Path.Combine(work, "trace.bin");
        File.WriteAllText(victim, "x");
        try
        {
            var planner = new ForceRemovalPlanner();
            var plan = new ForceRemovalPlan(
                Query: "trace",
                Hints: ["trace"],
                Targets:
                [
                    new ForceRemovalTarget(ForceRemovalKind.File, victim, "trace"),
                    new ForceRemovalTarget(ForceRemovalKind.File, @"C:\Windows\System32\notepad.exe", "win"),
                ]);

            var executor = new ForceRemovalExecutor(new Safety.QuarantineService(qRoot));
            var result = executor.Execute(plan, "job-force");

            result.Removed.Should().Be(1);
            result.Skipped.Should().BeGreaterThanOrEqualTo(1);
            File.Exists(victim).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(qRoot)) Directory.Delete(qRoot, true);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }
}

public class RulePackUpdaterTests
{
    [Fact]
    public void Update_from_file_with_hash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateRuleUpd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, "src.json");
        var dest = Path.Combine(dir, "dest.json");
        File.WriteAllText(src, """{"schemaVersion":1,"rules":[]}""");
        try
        {
            var updater = new RulePackUpdater();
            var noHash = updater.UpdateFromFile(src, dest);
            noHash.Success.Should().BeTrue(noHash.ErrorMessage);
            File.Exists(dest).Should().BeTrue();

            var badHash = updater.UpdateFromFile(src, dest, expectedSha256: "00");
            badHash.Success.Should().BeFalse();
            badHash.ErrorCode.Should().Be("E_HASH");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
