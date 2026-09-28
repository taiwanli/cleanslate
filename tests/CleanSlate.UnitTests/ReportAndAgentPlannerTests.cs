using CleanSlate.Core;
using CleanSlate.Safety;
using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class CleanupReportExporterTests
{
    [Fact]
    public void Exports_json_and_html()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateReportTests", Guid.NewGuid().ToString("N"));
        try
        {
            var exporter = new CleanupReportExporter();
            var report = new CleanupReport(
                JobId: "job-1",
                UtcTimestamp: DateTime.UtcNow,
                Mode: "Cleanup",
                TargetName: "Demo App",
                QuarantineRoot: @"C:\Temp\q",
                Succeeded: 1,
                Skipped: 1,
                Failed: 0,
                Items:
                [
                    new CleanupReportItem("File", @"C:\Users\x\AppData\Local\Demo\cache.bin", RiskLevel.Low, "Quarantined", "name match"),
                    new CleanupReportItem("Registry", @"HKCU\Software\Demo", RiskLevel.Medium, "Skipped", "needs elevation"),
                ]);

            var json = exporter.ExportJson(report, dir);
            var html = exporter.ExportHtml(report, dir);

            File.Exists(json).Should().BeTrue();
            File.Exists(html).Should().BeTrue();
            File.ReadAllText(html).Should().Contain("<html");
            File.ReadAllText(html).Should().Contain("job-1");
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

public class AgentCleanupPlannerTests
{
    [Fact]
    public void Expands_env_patterns_and_builds_layers()
    {
        var planner = new AgentRules.AgentCleanupPlanner();
        var rule = new AgentCleanupRule(
            AgentId: "demo",
            DisplayName: "Demo",
            ProcessNames: ["demo.exe"],
            PathPatterns: ["%APPDATA%\\DemoAgent\\cache", "%LOCALAPPDATA%\\DemoAgent\\**"],
            Layers:
            [
                new AgentCleanupLayer("L1Cache", RiskLevel.Low, true, "ok", "cache"),
                new AgentCleanupLayer("L4Credential", RiskLevel.High, false, "secret", "credentials"),
            ]);

        var plan = planner.BuildPlan(rule);
        plan.Should().HaveCount(4); // 2 roots 脳 2 layers
        plan.Should().Contain(p => p.LayerName == "L1Cache");
        plan.Should().Contain(p => p.LayerName == "L4Credential");
        plan.Select(p => p.TargetPath).Should().OnlyContain(p => !p.Contains('*'));
    }

    [Fact]
    public void Can_filter_layers()
    {
        var planner = new AgentRules.AgentCleanupPlanner();
        var rule = new AgentCleanupRule(
            AgentId: "demo",
            DisplayName: "Demo",
            ProcessNames: [],
            PathPatterns: ["%APPDATA%\\Demo"],
            Layers:
            [
                new AgentCleanupLayer("L1Cache", RiskLevel.Low, true, null, "cache"),
                new AgentCleanupLayer("L2Session", RiskLevel.Medium, false, null, "sessions"),
            ]);

        var plan = planner.BuildPlan(rule, onlySelectedLayers: true, selectedLayerNames: new HashSet<string> { "L1Cache" });
        plan.Should().OnlyContain(p => p.LayerName == "L1Cache");
    }
}



