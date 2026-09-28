using CleanSlate.Core;
using CleanSlate.Safety;
using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class UndoLogServiceTests
{
    [Fact]
    public void Job_append_list_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateUndoTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UndoLogService(dir);
            service.BeginJob("job-undo", "Cleanup", "Demo");
            service.Append("job-undo", new UndoLogEntry(
                "QuarantineFile",
                @"C:\Temp\a.txt",
                RiskLevel.Low,
                "Quarantined",
                @"C:\q\a.txt",
                "ok",
                DateTime.UtcNow));

            var job = service.Load("job-undo");
            job.Should().NotBeNull();
            job!.Items.Should().HaveCount(1);
            job.Items[0].Action.Should().Be("QuarantineFile");

            var jobs = service.ListJobs();
            jobs.Should().Contain(j => j.JobId == "job-undo");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Registry_backup_is_best_effort()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanSlateUndoTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UndoLogService(dir);
            // Should not throw even if reg export fails
            var act = () => service.BackupRegistryKey(@"HKCU\Software\Microsoft", "job-reg");
            act.Should().NotThrow();
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

public class BatchUninstallParserSmokeTests
{
    [Fact]
    public async Task Batch_service_skips_system_entries()
    {
        var service = new Uninstall.BatchUninstallService();
        var entry = new SoftwareEntry(
            Id: "sys",
            DisplayName: "System",
            Publisher: null,
            Version: null,
            Source: SoftwareSource.Registry,
            InstallLocation: null,
            UninstallString: "C:\\nope\\unins000.exe",
            EstimatedSize: 0,
            RiskTags: [],
            CanSilentUninstall: false,
            IsSystem: true);

        var summary = await service.RunAsync([entry]);
        summary.Skipped.Should().Be(1);
        summary.Succeeded.Should().Be(0);
        summary.Results[0].Code.Should().Be("E_PROTECTED");
    }
}
