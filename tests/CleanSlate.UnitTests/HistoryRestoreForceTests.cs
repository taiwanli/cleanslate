using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using CleanSlate.Safety;
using CleanSlate.AgentRules;

namespace CleanSlate.UnitTests;

public class HistoryStoreTests
{
    [Fact]
    public void Insert_and_list_jobs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateHistTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HistoryStore(Path.Combine(dir, "history.db"));
            store.InsertJob("h1", "Cleanup", "App", 2, 1, 0, "C:\\report.html");
            store.InsertItem("h1", "File", @"C:\tmp\a.bin", "Low", "Quarantined", null);

            var jobs = store.ListJobs();
            jobs.Should().Contain(j => j.JobId == "h1" && j.Succeeded == 2);
            var items = store.ListItems("h1");
            items.Should().HaveCount(1);
            items[0].PathOrKey.Should().Be(@"C:\tmp\a.bin");
            store.Dispose();
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { /* file lock race */ }
            }
        }
    }
}

public class QuarantineRestorerTests
{
    [Fact]
    public void Restore_job_folder_roundtrip()
    {
        var qRoot = Path.Combine(Path.GetTempPath(), "CleanSlateRestoreTests", Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "CleanSlateRestoreWork", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = Path.Combine(work, "keep.txt");
            File.WriteAllText(source, "data");
            var service = new QuarantineService(qRoot);
            var q = service.QuarantineFile(source, "jobR", "test");
            q.Success.Should().BeTrue();
            File.Exists(source).Should().BeFalse();

            var restorer = new QuarantineRestorer(service);
            var summary = restorer.RestoreJob("jobR");
            summary.Restored.Should().Be(1);
            File.Exists(source).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(qRoot)) Directory.Delete(qRoot, true);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }
}

public class RulePackWatcherTests
{
    [Fact]
    public void Loads_and_watches_rule_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateRuleWatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rules.json");
        File.WriteAllText(path, """
        {
          "schemaVersion": 1,
          "rules": [
            {
              "agentId": "x",
              "displayName": "X",
              "processNames": [],
              "pathPatterns": ["%APPDATA%\\X"],
              "layers": [{ "name": "L1Cache", "risk": "Low", "defaultSelected": true }]
            }
          ]
        }
        """);
        try
        {
            using var watcher = new RulePackWatcher(path);
            watcher.Current.Should().NotBeNull();
            watcher.Current!.Success.Should().BeTrue();
            watcher.Current.Rules.Should().HaveCount(1);
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

public class ForceRemovalTests
{
    [Fact]
    public void Force_removal_plan_does_not_throw()
    {
        var planner = new Uninstall.ForceRemovalPlanner();
        var act = () => planner.Build("TotallyUnknownAppXYZ123", []);
        act.Should().NotThrow();
    }
}

